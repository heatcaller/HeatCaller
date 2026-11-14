namespace HeatCaller.Tests;

/// <summary>
/// Who's Racing with several drivers picked (1 Nov 2026): the roster takes
/// Ctrl and Shift clicks, and checking the picked drivers in is timed like
/// every other desk change, so a slower phone tap cannot undo it.
/// </summary>
public sealed class CheckInMultiSelectTests : IDisposable
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

    private static (CheckInControl Page, CheckInStore CheckIns, List<DriverProfile> Drivers) Desk(bool withTech = false)
    {
        var profiles = ProfileStore.Load();
        var drivers = new List<DriverProfile>
        {
            new() { Transponder = "1001", DriverName = "Ana", ClassName = "Stock" },
            new() { Transponder = "1002", DriverName = "Ben", ClassName = "Stock" },
            new() { Transponder = "1003", DriverName = "Cy", ClassName = "Stock" },
        };
        profiles.UpsertMany(drivers);

        var checkIns = CheckInStore.Load();
        var page = withTech
            ? new CheckInControl(profiles, checkIns, RaceClassStore.Load(), new AppSettings(), new UndoService(), TechInspectionStore.Load())
            : new CheckInControl(profiles, checkIns, RaceClassStore.Load(), new AppSettings(), new UndoService());

        return (page, checkIns, drivers);
    }

    [Fact]
    public void The_roster_lets_several_drivers_be_picked()
    {
        var (page, _, _) = Desk();

        using (page)
        {
            var grid = Assert.Single(Everything(page).OfType<RosterGrid>());

            Assert.True(grid.MultiSelect);
            Assert.Equal(DataGridViewSelectionMode.FullRowSelect, grid.SelectionMode);
        }
    }

    [Fact]
    public void Checking_in_the_picked_drivers_ticks_each_in_and_times_each_as_a_desk_change()
    {
        var (page, checkIns, drivers) = Desk();

        using (page)
        {
            var timed = new List<string>();
            page.CheckInChangedAtDesk += timed.Add;

            // Ben was in already: still in, and timed with the rest.
            checkIns.SetCheckedIn("1002", true);

            page.CheckInDrivers(drivers.Take(2).ToList());

            Assert.True(checkIns.IsCheckedIn("1001"));
            Assert.True(checkIns.IsCheckedIn("1002"));
            Assert.False(checkIns.IsCheckedIn("1003"));
            Assert.Equal(new[] { "1001", "1002" }, timed);
        }
    }

    // --- Drivers picked apart from each other (1 Nov 2026) -----------------
    //
    // Asher Ctrl-picked drivers that were not next to each other in the list,
    // went to inspect them, and the inspection was for one driver only. Ana
    // and Cy are picked here, with Ben between them left out, and every way
    // into the bulk inspection has to come out with both of them.

    /// <summary>
    /// The page in a window of its own, off the side of the screen, with Ana
    /// and Cy picked and Ben between them not.
    /// </summary>
    private static Form PickedApart(CheckInControl page, out RosterGrid grid, out int ana, out int cy)
    {
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

        grid = Assert.Single(Everything(page).OfType<RosterGrid>());
        var rows = grid.Rows.Cast<DataGridViewRow>().ToList();

        ana = rows.Single(row => (row.Tag as DriverProfile)?.Transponder == "1001").Index;
        var ben = rows.Single(row => (row.Tag as DriverProfile)?.Transponder == "1002").Index;
        cy = rows.Single(row => (row.Tag as DriverProfile)?.Transponder == "1003").Index;

        // Ben has to sit between them for this to be a test of anything.
        Assert.True(Math.Min(ana, cy) < ben && ben < Math.Max(ana, cy), "Ben is not between Ana and Cy in the list.");

        grid.ClearSelection();
        grid.Rows[ana].Selected = true;
        grid.Rows[cy].Selected = true;
        StaThread.Settle();

        return host;
    }

    private static string[] Transponders(IEnumerable<DriverProfile> drivers) =>
        drivers.Select(driver => driver.Transponder).ToArray();

    [Fact]
    public void Inspect_selected_cars_takes_every_picked_driver_when_they_are_not_next_to_each_other() =>
        StaThread.Run(() =>
        {
            var (page, _, _) = Desk(withTech: true);

            using (page)
            using (PickedApart(page, out _, out _, out _))
            {
                IReadOnlyList<DriverProfile>? asked = null;
                page.InspectManyRequested += drivers => asked = drivers;
                page.InspectRequested += profile => Assert.Fail($"Only {profile.DriverName} was sent to be inspected.");

                // As the button reads: the button layer drops a trailing "..." (AppTheme.Caption).
                var button = Assert.Single(Everything(page).OfType<Button>(), b => b.Text == "Inspect Selected Cars");
                button.PerformClick();

                Assert.NotNull(asked);
                Assert.Equal(new[] { "1001", "1003" }, Transponders(asked!));
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => "the Inspect Selected Cars button");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_double_click_on_a_picked_driver_takes_every_picked_driver_when_they_are_not_next_to_each_other(bool ctrl) =>
        StaThread.Run(() =>
        {
            var (page, checkIns, _) = Desk(withTech: true);

            using (page)
            using (PickedApart(page, out var grid, out var ana, out var cy))
            {
                // The double-click's first press: the picks are remembered,
                // then an ordinary click narrows them to Cy's row alone (with
                // Ctrl held, it takes Cy out instead).
                grid.RememberSelection();
                grid.ClearSelection();

                if (!ctrl)
                {
                    grid.Rows[cy].Selected = true;
                }
                else
                {
                    grid.Rows[ana].Selected = true;
                }

                var drivers = page.PickForDoubleClick(cy, ctrl ? Keys.Control : Keys.None);

                Assert.Equal(new[] { "1001", "1003" }, Transponders(drivers));

                // Both picked again, for the menu it opens.
                Assert.Equal(new[] { ana, cy }.Order(),
                    grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).Order());

                // The menu's Check In: both in, Ben left alone.
                page.CheckInDrivers(drivers);

                Assert.True(checkIns.IsCheckedIn("1001"));
                Assert.False(checkIns.IsCheckedIn("1002"));
                Assert.True(checkIns.IsCheckedIn("1003"));
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => "a double-click on a picked driver");

    [Fact]
    public void A_ctrl_double_click_on_a_driver_not_yet_picked_adds_them_to_the_ones_picked() =>
        StaThread.Run(() =>
        {
            var (page, _, _) = Desk(withTech: true);

            using (page)
            using (PickedApart(page, out var grid, out var ana, out var cy))
            {
                var ben = grid.Rows.Cast<DataGridViewRow>()
                    .Single(row => (row.Tag as DriverProfile)?.Transponder == "1002").Index;

                // A Ctrl+click on Ben puts Ben in with the other two.
                grid.RememberSelection();
                grid.Rows[ben].Selected = true;

                var drivers = page.PickForDoubleClick(ben, Keys.Control);

                Assert.Equal(new[] { "1001", "1002", "1003" }, Transponders(drivers));
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => "a Ctrl+double-click on a driver not yet picked");

    [Fact]
    public void A_right_click_on_one_of_the_picked_drivers_keeps_them_all_picked() =>
        StaThread.Run(() =>
        {
            var (page, _, _) = Desk();

            using (page)
            using (PickedApart(page, out var grid, out _, out var cy))
            {
                Assert.Equal(new[] { "1001", "1003" }, Transponders(page.PickForMenu(cy, Keys.None)));
                Assert.Equal(2, grid.SelectedRows.Count);
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => "a right-click on a picked driver");

    [Fact]
    public void A_right_click_on_a_driver_not_picked_picks_them_alone_and_ctrl_adds_them() =>
        StaThread.Run(() =>
        {
            var (page, _, _) = Desk();

            using (page)
            using (PickedApart(page, out var grid, out _, out _))
            {
                var ben = grid.Rows.Cast<DataGridViewRow>().Single(row => (row.Tag as DriverProfile)?.Transponder == "1002").Index;

                Assert.Equal(new[] { "1001", "1002", "1003" }, Transponders(page.PickForMenu(ben, Keys.Control)).Order());

                grid.Rows[ben].Selected = false;
                Assert.Equal(new[] { "1002" }, Transponders(page.PickForMenu(ben, Keys.None)));

                // Not left on the Checked In box, which a current cell puts in edit mode.
                Assert.Equal("driver", grid.CurrentCell?.OwningColumn?.Name);
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => "a right-click on a driver not picked");

    [Fact]
    public void Checking_out_the_picked_drivers_takes_each_off_and_times_each_as_a_desk_change()
    {
        var (page, checkIns, drivers) = Desk();

        using (page)
        {
            var timed = new List<string>();
            checkIns.CheckInAll(drivers.Select(p => p.Transponder).ToList());
            page.CheckInChangedAtDesk += timed.Add;

            page.CheckOutDrivers(drivers.Take(2).ToList());

            Assert.False(checkIns.IsCheckedIn("1001"));
            Assert.False(checkIns.IsCheckedIn("1002"));
            Assert.True(checkIns.IsCheckedIn("1003"));
            Assert.Equal(new[] { "1001", "1002" }, timed);
        }
    }
}
