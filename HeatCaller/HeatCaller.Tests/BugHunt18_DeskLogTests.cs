using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 18: Desk mode's "Tonight at the Desk" log said "checked out from
/// a phone" for the desk's own changes - the first click of a double-click put
/// back, a tick from Set Up Tonight, a tick refused on a closed day - because
/// those refreshed the marks the way a phone's tap does.
/// </summary>
public sealed class BugHunt18_DeskLogTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static IEnumerable<Control> Everything(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var inner in Everything(child))
            {
                yield return inner;
            }
        }
    }

    private static (CheckInControl Page, CheckInStore CheckIns, Form Host, RosterGrid Grid) Desk()
    {
        var profiles = ProfileStore.Load();
        profiles.UpsertMany(new List<DriverProfile>
        {
            new() { Transponder = "1001", DriverName = "Ana", ClassName = "Stock" },
            new() { Transponder = "1002", DriverName = "Ben", ClassName = "Stock" },
        });

        var checkIns = CheckInStore.Load();
        var page = new CheckInControl(profiles, checkIns, RaceClassStore.Load(), new AppSettings(), new UndoService());

        var host = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ClientSize = new Size(1200, 700),
        };

        page.Dock = DockStyle.Fill;
        host.Controls.Add(page);
        host.Show();
        StaThread.Settle();

        return (page, checkIns, host, Assert.Single(Everything(page).OfType<RosterGrid>()));
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 18, desk log");

    private static void Call(CheckInControl page, string method, params object[] args) =>
        typeof(CheckInControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);

    private static int RowOf(RosterGrid grid, string transponder) =>
        grid.Rows.Cast<DataGridViewRow>().Single(row => (row.Tag as DriverProfile)?.Transponder == transponder).Index;

    [Fact]
    public void The_first_click_of_a_double_click_put_back_is_not_logged_as_a_phone() =>
        OnUiThread(() =>
        {
            var (page, checkIns, host, grid) = Desk();

            using (page)
            using (host)
            {
                var ana = RowOf(grid, "1001");

                // The first click: remembered, then the tick it makes.
                Call(page, "OnGridClickStarting", ana);
                grid.Rows[ana].Cells[0].Value = true;
                Assert.True(checkIns.IsCheckedIn("1001"));

                // The double-click puts it back.
                Call(page, "UndoFirstClickTick");

                Assert.False(checkIns.IsCheckedIn("1001"));
                Assert.Equal(false, grid.Rows[ana].Cells[0].Value);
                Assert.DoesNotContain(page.DeskLog, entry => entry.What.Contains("phone", StringComparison.Ordinal));
                Assert.DoesNotContain(page.DeskLog, entry => entry.Who == "Ana");
            }
        });

    [Fact]
    public void A_desk_refresh_does_not_log_a_phone_but_a_phone_refresh_still_does() =>
        OnUiThread(() =>
        {
            var (page, checkIns, host, _) = Desk();

            using (page)
            using (host)
            {
                // Set Up Tonight's Drivers step ticks Ana in.
                checkIns.SetCheckedIn("1001", true);
                page.RefreshMarksFromDesk();
                Assert.Empty(page.DeskLog);

                // A phone ticks Ben in.
                checkIns.SetCheckedIn("1002", true);
                page.RefreshCheckInMarks();
                var entry = Assert.Single(page.DeskLog);
                Assert.Equal("checked in from a phone", entry.What);
            }
        });

    [Fact]
    public void Set_Up_Tonight_refreshes_the_marks_as_the_desk()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "MainForm.RaceSetup.cs"));
        var start = source.IndexOf("private void SetCheckedInFromWizard(", StringComparison.Ordinal);
        Assert.True(start > 0);

        var body = source[start..source.IndexOf("\n    }", start, StringComparison.Ordinal)];
        Assert.Contains("_checkInControl?.RefreshMarksFromDesk();", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshCheckInMarks", body, StringComparison.Ordinal);
    }
}
