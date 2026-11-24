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
