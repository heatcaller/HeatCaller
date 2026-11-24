using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Practice's columns and Dock Inbox, as Race Control has them (24 Nov 2026,
/// asked for by Asher: "on the practice screen, implement the same custom
/// columns thing we did on race control then remove the columns button.
/// also add the same button to dock the inbox on practice"). On the real
/// page in the real window. The columns menu itself is not shown - its two
/// steps are what it calls (BeginColumnsVisit, EndColumnsVisit).
/// </summary>
public sealed class PracticeColumnsAndDockTests
{
    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Settings() => new AppSettings
    {
        StartMaximized = false,
        HasCompletedFirstRun = true,
        HasSeenRaceDaySetup = true,
        AutoBackupEnabled = false,
        ConnectDecodersAtStartup = false,
    }.Save();

    [Fact]
    public void A_visit_changes_the_table_as_it_goes_saves_on_close_and_undoes_in_one_step()
    {
        var seen = StaThread.Run(() =>
        {
            using var data = new TempAppData();
            Settings();

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.Show();
                StaThread.Settle();

                var page = Field<PracticeModeControl>(window, "_practiceControl");
                var grid = Field<DataGridView>(page, "_grid");
                var undo = Field<UndoService>(page, "_undo");
                string[] Table() => grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Name).ToArray();

                var extra = KpiCatalog.All.First(d => !KpiCatalog.DefaultPractice.Contains(d.Key, StringComparer.OrdinalIgnoreCase)).Key;
                var before = Table();
                var stepsBefore = undo.PendingDescriptions.Count;

                var visit = page.BeginColumnsVisit();
                visit.Choices.Toggle(extra);
                var during = Table();
                var stepsDuring = undo.PendingDescriptions.Count;

                page.EndColumnsVisit(visit);
                var saved = AppSettings.Load().PracticeKpis.ToArray();
                var stepsAfter = undo.PendingDescriptions.ToList();

                undo.Undo();
                var afterUndo = Table();

                // A visit that changes nothing puts nothing on Undo.
                var idle = page.BeginColumnsVisit();
                idle.Choices.Toggle(extra);
                idle.Choices.Toggle(extra);
                page.EndColumnsVisit(idle);

                return (extra, before, during, stepsBefore, stepsDuring, saved, stepsAfter, afterUndo, stepsAfterIdle: undo.PendingDescriptions.Count);
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(2), () => "a visit to Practice's columns drop-down");

        Assert.Equal(KpiCatalog.DefaultPractice, seen.before);
        Assert.Contains(seen.extra, seen.during);
        Assert.Equal(seen.stepsBefore, seen.stepsDuring);
        Assert.Contains(seen.extra, seen.saved);
        Assert.Equal(seen.stepsBefore + 1, seen.stepsAfter.Count);
        Assert.Contains("Change the Practice columns", seen.stepsAfter);
        Assert.Equal(KpiCatalog.DefaultPractice, seen.afterUndo);
        Assert.Equal(seen.stepsBefore, seen.stepsAfterIdle);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc proc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>The columns list, if one is open: a drop-down holding a ColumnChecklist.</summary>
    private static ToolStripDropDown? OpenColumnsList()
    {
        ToolStripDropDown? found = null;

        EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
        {
            if (Control.FromHandle(hwnd) is ToolStripDropDown { Visible: true } drop
                && drop.Items.OfType<ToolStripControlHost>().Any(h => h.Control.Controls.OfType<ColumnChecklist>().Any()))
            {
                found = drop;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// The button is gone, so the right-click is the only way in: a real one
    /// - sent to the table's window, as the mouse would - opens the list, on
    /// a heading and on the table's empty body alike (24 Nov 2026).
    /// </summary>
    [Fact]
    public void A_right_click_on_a_heading_or_the_empty_table_opens_the_columns_list()
    {
        var seen = StaThread.Run(() =>
        {
            using var data = new TempAppData();
            Settings();

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.Show();
                window.ClientSize = new Size(1280, 800);
                typeof(MainForm).GetMethod("GoToPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { "Practice" });
                StaThread.Settle();

                var page = Field<PracticeModeControl>(window, "_practiceControl");
                var grid = Field<DataGridView>(page, "_grid");
                var results = new List<bool>();

                foreach (var at in new[] { new Point(grid.Columns[0].Width + 10, grid.ColumnHeadersHeight / 2), new Point(grid.Width / 2, grid.Height / 2) })
                {
                    var lParam = (IntPtr)((at.Y << 16) | (at.X & 0xFFFF));
                    SendMessage(grid.Handle, 0x0204, (IntPtr)0x0002, lParam);
                    SendMessage(grid.Handle, 0x0205, IntPtr.Zero, lParam);
                    StaThread.Settle();

                    var list = OpenColumnsList();
                    results.Add(list is not null);
                    list?.Close();
                    StaThread.Settle();
                }

                return results;
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(2), () => "right-clicking Practice's table");

        Assert.Equal(new[] { true, true }, seen);
    }

    [Fact]
    public void The_Choose_Columns_button_is_gone_and_Dock_Inbox_stands_beside_Expand_and_docks_it()
    {
        var seen = StaThread.Run(() =>
        {
            using var data = new TempAppData();
            Settings();

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.Show();
                StaThread.Settle();

                // On screen, as it is when the button is pressed.
                typeof(MainForm).GetMethod("GoToPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { "Practice" });
                StaThread.Settle();

                var page = Field<PracticeModeControl>(window, "_practiceControl");
                var raceControl = Field<RaceControlControl>(window, "_raceControlControl");
                var expand = Field<Button>(page, "_btnExpand");
                var dock = page.DockInboxButton;

                static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));
                var columnsButton = All(page).OfType<Button>().Any(b => b.Text.StartsWith("Choose Columns", StringComparison.Ordinal));

                var beside = ReferenceEquals(dock.Parent, expand.Parent);
                var picture = dock.Text.Length == 0 && dock.Width == dock.Height;
                var before = dock.AccessibleName;

                dock.PerformClick();
                StaThread.Settle();

                var inbox = (Form?)typeof(MainForm).GetField("_inboxForm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

                return (columnsButton, beside, picture, before, docked: inbox is { IsDisposed: false, TopLevel: false },
                    practice: dock.AccessibleName, raceControl: raceControl.DockInboxButton.AccessibleName);
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(2), () => "Practice's Dock Inbox");

        Assert.False(seen.columnsButton, "the columns are chosen by right-clicking the table now");
        Assert.True(seen.beside, "Dock Inbox is on Expand's row");
        Assert.True(seen.picture, "a square picture, as on Race Control");
        Assert.Equal("Dock Inbox", seen.before);
        Assert.True(seen.docked, "a press docks the inbox, opening it");
        Assert.Equal("Undock Inbox", seen.practice);
        Assert.Equal("Undock Inbox", seen.raceControl);
    }
}
