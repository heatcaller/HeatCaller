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

    private static (CheckInControl Page, CheckInStore CheckIns, List<DriverProfile> Drivers) Desk()
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
        var page = new CheckInControl(profiles, checkIns, RaceClassStore.Load(), new AppSettings(), new UndoService());

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
}
