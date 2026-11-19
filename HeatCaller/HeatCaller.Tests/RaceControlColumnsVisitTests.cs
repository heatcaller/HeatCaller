using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Race Control's columns drop-down (15 Nov 2026), on the real page: a tick
/// or a move goes on the board at once, closing saves, and Undo takes the
/// whole visit back in one step. The menu itself is not shown - the visit's
/// two steps are what it calls (BeginColumnsVisit, EndColumnsVisit).
/// </summary>
public sealed class RaceControlColumnsVisitTests
{
    [Fact]
    public void A_visit_changes_the_board_as_it_goes_saves_on_close_and_undoes_in_one_step()
    {
        var seen = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            new AppSettings
            {
                StartMaximized = false,
                HasCompletedFirstRun = true,
                HasSeenRaceDaySetup = true,
                AutoBackupEnabled = false,
                ConnectDecodersAtStartup = false,
            }.Save();

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.Show();
                StaThread.Settle();

                var page = Field<RaceControlControl>(window, "_raceControlControl");
                var grid = Field<DataGridView>(page, "_dgvLeaderboard");
                var undo = Field<UndoService>(page, "_undo");
                string[] Board() => grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Name).ToArray();

                var before = Board();
                var stepsBefore = undo.PendingDescriptions.Count;

                var visit = page.BeginColumnsVisit();
                visit.Choices.Toggle("last");
                visit.Choices.Move("last", -1);
                var duringVisit = Board();
                var stepsDuring = undo.PendingDescriptions.Count;

                page.EndColumnsVisit(visit);
                var saved = AppSettings.Load().RaceControlKpis.ToArray();
                var stepsAfter = undo.PendingDescriptions.ToList();

                undo.Undo();
                var afterUndo = Board();

                // A visit that changes nothing puts nothing on Undo.
                var idle = page.BeginColumnsVisit();
                idle.Choices.Toggle("last");
                idle.Choices.Toggle("last");
                page.EndColumnsVisit(idle);
                var stepsAfterIdle = undo.PendingDescriptions.Count;

                return (before, duringVisit, stepsBefore, stepsDuring, saved, stepsAfter, afterUndo, stepsAfterIdle);
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(2), () => "a visit to the columns drop-down");

        Assert.Equal(KpiCatalog.DefaultRaceControl, seen.before);

        var expected = KpiCatalog.DefaultRaceControl.Take(6).Append("last").Append(KpiCatalog.DefaultRaceControl[6]).ToArray();
        Assert.Equal(expected, seen.duringVisit);
        Assert.Equal(seen.stepsBefore, seen.stepsDuring);

        Assert.Equal(expected, seen.saved);
        Assert.Equal(seen.stepsBefore + 1, seen.stepsAfter.Count);
        Assert.Contains("Change the Race Control columns", seen.stepsAfter);

        Assert.Equal(KpiCatalog.DefaultRaceControl, seen.afterUndo);
        Assert.Equal(seen.stepsBefore, seen.stepsAfterIdle);
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
