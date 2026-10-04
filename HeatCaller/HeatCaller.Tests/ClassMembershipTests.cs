namespace HeatCaller.Tests;

/// <summary>
/// A driver in more than one class (DriverProfile.Classes), and the two
/// things a class rename used to leave behind: garage cars and a race day's
/// auto-advance pin (4 Oct 2026).
/// </summary>
public sealed class ClassMembershipTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    //  Which classes a driver races
    // ------------------------------------------------------------------

    [Fact]
    public void A_driver_races_their_main_class_and_their_others_main_first_each_once()
    {
        var driver = new DriverProfile
        {
            Transponder = "1001",
            ClassName = "Stock",
            Classes = new List<string> { "Modified", " stock ", "", "Novice" },
        };

        Assert.Equal(new[] { "Stock", "Modified", "Novice" }, driver.AllClasses);
        Assert.True(driver.RunsClass("stock"));
        Assert.True(driver.RunsClass(" Modified "));
        Assert.False(driver.RunsClass("Touring"));
        Assert.False(driver.RunsClass(""));
        Assert.False(driver.RunsClass(null));
    }

    [Fact]
    public void A_profile_from_before_the_list_races_its_one_class()
    {
        var driver = new DriverProfile { Transponder = "1002", ClassName = "Stock" };

        Assert.Equal(new[] { "Stock" }, driver.AllClasses);
        Assert.True(driver.RunsClass("Stock"));
    }

    [Fact]
    public void A_driver_with_no_main_class_still_races_their_others()
    {
        var driver = new DriverProfile { Transponder = "1003", Classes = new List<string> { "Modified" } };

        Assert.Equal(new[] { "Modified" }, driver.AllClasses);
        Assert.True(driver.RunsClass("Modified"));
    }

    [Fact]
    public void The_other_classes_are_saved_and_read_back()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile
        {
            Transponder = "1004",
            DriverName = "Sam",
            ClassName = "Stock",
            Classes = new List<string> { "Modified" },
        });

        var again = ProfileStore.Load().Get("1004")!;

        Assert.Equal(new[] { "Stock", "Modified" }, again.AllClasses);
    }

    // ------------------------------------------------------------------
    //  A rename carries a driver's other classes too
    // ------------------------------------------------------------------

    [Fact]
    public void Renaming_a_class_moves_it_in_a_drivers_other_classes_and_undo_moves_it_back()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile
        {
            Transponder = "2001",
            DriverName = "Alex",
            ClassName = "Stock",
            Classes = new List<string> { "Modified" },
        });

        var already = profiles.WithClassName("Mod 17.5");
        var moved = profiles.RenameClass("Modified", "Mod 17.5");

        Assert.Equal(new[] { "2001" }, moved);
        Assert.Equal(new[] { "Stock", "Mod 17.5" }, ProfileStore.Load().Get("2001")!.AllClasses);

        profiles.UndoRenameClass("Mod 17.5", "Modified", already);

        Assert.Equal(new[] { "Stock", "Modified" }, ProfileStore.Load().Get("2001")!.AllClasses);
    }

    [Fact]
    public void A_driver_already_in_the_new_name_before_the_rename_is_left_there_by_the_undo()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "2002", DriverName = "Jo", ClassName = "Stock", Classes = new List<string> { "Mod 17.5" } });
        profiles.Upsert(new DriverProfile { Transponder = "2003", DriverName = "Kim", ClassName = "Modified" });

        var already = profiles.WithClassName("Mod 17.5");
        profiles.RenameClass("Modified", "Mod 17.5");
        profiles.UndoRenameClass("Mod 17.5", "Modified", already);

        var reloaded = ProfileStore.Load();
        Assert.Equal(new[] { "Stock", "Mod 17.5" }, reloaded.Get("2002")!.AllClasses);
        Assert.Equal("Modified", reloaded.Get("2003")!.ClassName);
    }

    // ------------------------------------------------------------------
    //  Garage cars follow a rename
    // ------------------------------------------------------------------

    [Fact]
    public void Renaming_a_class_moves_the_garage_cars_in_it_and_undo_moves_them_back()
    {
        var cars = CarStore.Load();
        var stock = new Car { Name = "Red Audi", ClassName = "Stock" };
        var other = new Car { Name = "Blue NSX", ClassName = "Modified" };
        var leftover = new Car { Name = "Old one", ClassName = "Stock 17.5" };
        cars.Upsert(stock);
        cars.Upsert(other);
        cars.Upsert(leftover);

        var already = cars.WithClassName("Stock 17.5");
        var moved = cars.RenameClass("Stock", "Stock 17.5");

        Assert.Equal(new[] { stock.Id }, moved);
        Assert.Equal("Stock 17.5", CarStore.Load().Get(stock.Id)!.ClassName);
        Assert.Equal("Modified", CarStore.Load().Get(other.Id)!.ClassName);

        cars.UndoRenameClass("Stock 17.5", "Stock", already);

        var reloaded = CarStore.Load();
        Assert.Equal("Stock", reloaded.Get(stock.Id)!.ClassName);
        Assert.Equal("Stock 17.5", reloaded.Get(leftover.Id)!.ClassName);
    }

    // ------------------------------------------------------------------
    //  A race day's auto-advance pin follows a rename
    // ------------------------------------------------------------------

    [Fact]
    public void Renaming_a_class_moves_a_race_days_auto_advance_pin_and_undo_moves_it_back()
    {
        var events = RaceEventStore.Load();
        var pinned = new RaceEvent { Name = "Tuesday", AutoAdvanceScope = RoundPicker.ByClass, AutoAdvanceClassName = "Stock" };
        var elsewhere = new RaceEvent { Name = "Saturday", AutoAdvanceScope = RoundPicker.ByClass, AutoAdvanceClassName = "Stock 17.5" };
        events.Upsert(pinned);
        events.Upsert(elsewhere);

        var already = events.WithPinnedClass("Stock 17.5");
        var moved = events.RenamePinnedClass("Stock", "Stock 17.5");

        Assert.Equal(new[] { pinned.Id }, moved);
        Assert.Equal("Stock 17.5", RaceEventStore.Load().Get(pinned.Id)!.AutoAdvanceClassName);

        events.UndoRenamePinnedClass("Stock 17.5", "Stock", already);

        var reloaded = RaceEventStore.Load();
        Assert.Equal("Stock", reloaded.Get(pinned.Id)!.AutoAdvanceClassName);
        Assert.Equal("Stock 17.5", reloaded.Get(elsewhere.Id)!.AutoAdvanceClassName);
    }
}
