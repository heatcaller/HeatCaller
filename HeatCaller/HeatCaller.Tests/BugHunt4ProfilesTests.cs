using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 4: drivers and classes.
///
/// A driver listed twice in an imported sheet - a driver in two classes -
/// keeps the first row's class and car as their main ones and gets the
/// second row's as others (DriverImport.MergeClass, MergeCar). Importing the
/// same sheet again used to move them into their second class and out of
/// their first, and the second row's car replaced the first row's.
///
/// Undoing a class rename is refused when another class has been given the
/// old name since (RaceClassStore.UndoRename), and puts a driver's main class
/// and other classes back one at a time, so a driver who carried the new name
/// in one of them before the rename still gets the other back.
///
/// The undo of a quick photo change only changes the same driver, never
/// somebody given their transponder since (ProfileStore.CurrentVersionOf).
/// </summary>
public sealed class BugHunt4ProfilesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- profiles-1: a repeated row's class --------------------------------------------

    [Fact]
    public void Importing_a_two_class_drivers_rows_again_keeps_them_in_both_classes()
    {
        // As the first import left Jo: Stock, and Mod as another class.
        var jo = new DriverProfile { Transponder = "100", DriverName = "Jo", ClassName = "Stock", Classes = new List<string> { "Mod" } };

        DriverImport.MergeClass(jo, repeatRow: false, "Stock");
        DriverImport.MergeClass(jo, repeatRow: true, "Mod");

        Assert.Equal("Stock", jo.ClassName);
        Assert.Equal(new[] { "Stock", "Mod" }, jo.AllClasses);
    }

    [Fact]
    public void A_drivers_second_row_in_another_class_adds_it_and_a_repeat_of_either_changes_nothing()
    {
        var sam = new DriverProfile { Transponder = "200", DriverName = "Sam" };

        DriverImport.MergeClass(sam, repeatRow: false, "Stock");
        DriverImport.MergeClass(sam, repeatRow: true, "Mod");
        DriverImport.MergeClass(sam, repeatRow: true, "mod");
        DriverImport.MergeClass(sam, repeatRow: true, "Stock");
        DriverImport.MergeClass(sam, repeatRow: true, "");

        Assert.Equal("Stock", sam.ClassName);
        Assert.Equal(new[] { "Mod" }, sam.Classes);
    }

    [Fact]
    public void A_drivers_first_row_sets_the_main_class_and_a_blank_class_cell_changes_nothing()
    {
        var kim = new DriverProfile { Transponder = "300", DriverName = "Kim", ClassName = "Stock" };

        DriverImport.MergeClass(kim, repeatRow: false, "");
        Assert.Equal("Stock", kim.ClassName);

        DriverImport.MergeClass(kim, repeatRow: false, "Mod");
        Assert.Equal("Mod", kim.ClassName);

        // No main class yet: a repeat row gives them one.
        var lee = new DriverProfile { Transponder = "400", DriverName = "Lee" };
        DriverImport.MergeClass(lee, repeatRow: true, "Mod");

        Assert.Equal("Mod", lee.ClassName);
        Assert.Empty(lee.Classes);
    }

    // --- profiles-2: a repeated row's car ----------------------------------------------

    [Fact]
    public void A_drivers_second_row_adds_its_car_and_the_first_rows_stays_the_active_one()
    {
        var sam = new DriverProfile { Transponder = "200", DriverName = "Sam" };

        DriverImport.MergeCar(sam, repeatRow: false, "Losi Stock");
        DriverImport.MergeCar(sam, repeatRow: true, "Kyosho Mod");
        DriverImport.MergeCar(sam, repeatRow: true, "kyosho mod");
        DriverImport.MergeCar(sam, repeatRow: true, "");

        Assert.Equal("Losi Stock", sam.CarModel);
        Assert.Equal(new[] { "Losi Stock", "Kyosho Mod" }, sam.AllCars);
    }

    [Fact]
    public void A_drivers_first_row_still_sets_the_active_car()
    {
        var kim = new DriverProfile { Transponder = "300", DriverName = "Kim", CarModel = "Old car" };

        DriverImport.MergeCar(kim, repeatRow: false, "");
        Assert.Equal("Old car", kim.CarModel);

        DriverImport.MergeCar(kim, repeatRow: false, "New car");
        Assert.Equal("New car", kim.CarModel);
    }

    [Fact]
    public void Both_cars_from_a_drivers_two_rows_reach_the_garage()
    {
        var store = ProfileStore.Load();
        var raised = new List<string>();
        store.CarsAdded += (_, cars) => raised.AddRange(cars);

        var sam = new DriverProfile { Transponder = "200", DriverName = "Sam" };
        DriverImport.MergeCar(sam, repeatRow: false, "Losi Stock");
        DriverImport.MergeClass(sam, repeatRow: false, "Stock");
        DriverImport.MergeCar(sam, repeatRow: true, "Kyosho Mod");
        DriverImport.MergeClass(sam, repeatRow: true, "Mod");
        store.UpsertMany(new[] { sam });

        Assert.Equal(new[] { "Losi Stock", "Kyosho Mod" }, raised);

        var again = ProfileStore.Load().Get("200")!;
        Assert.Equal(new[] { "Losi Stock", "Kyosho Mod" }, again.AllCars);
        Assert.Equal(new[] { "Stock", "Mod" }, again.AllClasses);
    }

    // --- profiles-3: a rename's undo and a class given the old name since ---------------

    [Fact]
    public void Undoing_a_rename_is_refused_when_a_new_class_has_the_old_name_and_nothing_moves()
    {
        var (classes, profiles, cascade) = Stores();
        var stock = AddClass(classes, "Stock");
        profiles.Upsert(new DriverProfile { Transponder = "1", DriverName = "Jo", ClassName = "Stock" });

        var undo = new UndoService();
        var failures = new List<string>();
        undo.UndoFailed += (_, ex) => failures.Add(ex.Message);

        RenameInEditor(undo, classes, cascade, stock, "Stock 17.5");
        AddClass(classes, "Stock");

        Assert.Null(undo.Undo());

        Assert.Single(failures);
        Assert.Contains("\"Stock\"", failures[0]);
        Assert.Single(classes.All, c => c.Name == "Stock");
        Assert.Equal("Stock 17.5", classes.Get(stock.Id)!.Name);
        Assert.Equal("Stock 17.5", ProfileStore.Load().Get("1")!.ClassName);
        AssertNamesUnique(RaceClassStore.Load());
    }

    [Fact]
    public void Undoing_a_rename_still_puts_the_name_back_when_no_other_class_has_it()
    {
        var (classes, profiles, cascade) = Stores();
        var stock = AddClass(classes, "Stock");
        profiles.Upsert(new DriverProfile { Transponder = "1", DriverName = "Jo", ClassName = "Stock" });

        var undo = new UndoService();
        RenameInEditor(undo, classes, cascade, stock, "Stock 17.5");
        AddClass(classes, "Novice");
        RenameInEditor(undo, classes, cascade, classes.Get(stock.Id)!, "stock 17.5");

        Assert.Equal(2, undo.UndoSteps(2).Count);

        Assert.Equal("Stock", classes.Get(stock.Id)!.Name);
        Assert.Equal("Stock", ProfileStore.Load().Get("1")!.ClassName);
        AssertNamesUnique(classes);
    }

    [Fact]
    public void Two_classes_that_swapped_names_get_their_own_back_one_undo_at_a_time()
    {
        var (classes, profiles, cascade) = Stores();
        var stock = AddClass(classes, "Stock");
        var mod = AddClass(classes, "Mod");
        profiles.Upsert(new DriverProfile { Transponder = "1", DriverName = "Jo", ClassName = "Stock" });
        profiles.Upsert(new DriverProfile { Transponder = "2", DriverName = "Kim", ClassName = "Mod" });
        profiles.Upsert(new DriverProfile { Transponder = "3", DriverName = "Lee", ClassName = "Stock", Classes = new List<string> { "Mod" } });

        var undo = new UndoService();
        RenameInEditor(undo, classes, cascade, stock, "Spare");
        RenameInEditor(undo, classes, cascade, mod, "Stock");
        RenameInEditor(undo, classes, cascade, classes.Get(stock.Id)!, "Mod");

        Assert.Equal(new[] { "Mod", "Stock", "Mod" },
            new[] { "1", "2", "3" }.Select(t => profiles.Get(t)!.ClassName));

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(undo.Undo());
            AssertNamesUnique(classes);
        }

        Assert.Equal("Stock", classes.Get(stock.Id)!.Name);
        Assert.Equal("Mod", classes.Get(mod.Id)!.Name);

        var reloaded = ProfileStore.Load();
        Assert.Equal(new[] { "Stock" }, reloaded.Get("1")!.AllClasses);
        Assert.Equal(new[] { "Mod" }, reloaded.Get("2")!.AllClasses);
        Assert.Equal(new[] { "Stock", "Mod" }, reloaded.Get("3")!.AllClasses);
    }

    // --- profiles-4: a driver with the new name in one field and the old in the other ---

    [Fact]
    public void Undoing_a_rename_puts_back_the_main_class_of_a_driver_who_also_raced_the_new_name()
    {
        var (_, profiles, cascade) = Stores();
        profiles.Upsert(new DriverProfile { Transponder = "1", DriverName = "Jo", ClassName = "Modified", Classes = new List<string> { "Mod" } });

        var receipt = cascade.Rename("Modified", "Mod");
        Assert.Equal(new[] { "Mod" }, profiles.Get("1")!.AllClasses);

        cascade.Undo(receipt);

        var jo = ProfileStore.Load().Get("1")!;
        Assert.Equal("Modified", jo.ClassName);
        Assert.Equal(new[] { "Modified", "Mod" }, jo.AllClasses);
    }

    [Fact]
    public void Undoing_a_rename_puts_back_the_other_class_of_a_driver_whose_main_class_had_the_new_name()
    {
        var (_, profiles, cascade) = Stores();
        profiles.Upsert(new DriverProfile { Transponder = "1", DriverName = "Jo", ClassName = "Mod", Classes = new List<string> { "Modified" } });

        var receipt = cascade.Rename("Modified", "Mod");
        cascade.Undo(receipt);

        var jo = ProfileStore.Load().Get("1")!;
        Assert.Equal("Mod", jo.ClassName);
        Assert.Equal(new[] { "Mod", "Modified" }, jo.AllClasses);
    }

    [Fact]
    public void The_profile_store_leaves_a_drivers_main_class_and_other_classes_alone_separately()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "1", DriverName = "Jo", ClassName = "Modified", Classes = new List<string> { "Mod" } });
        profiles.Upsert(new DriverProfile { Transponder = "2", DriverName = "Kim", ClassName = "Mod", Classes = new List<string> { "Modified" } });

        var leaveMain = profiles.WithMainClassName("Mod");
        var leaveOther = profiles.WithOtherClassName("Mod");
        Assert.Equal(new[] { "2" }, leaveMain);
        Assert.Equal(new[] { "1" }, leaveOther);

        profiles.RenameClass("Modified", "Mod");
        profiles.UndoRenameClass("Mod", "Modified", leaveMain, leaveOther);

        var reloaded = ProfileStore.Load();
        Assert.Equal(new[] { "Modified", "Mod" }, reloaded.Get("1")!.AllClasses);
        Assert.Equal(new[] { "Mod", "Modified" }, reloaded.Get("2")!.AllClasses);
    }

    [Fact]
    public void A_drivers_classes_come_back_through_two_renames_and_both_undos()
    {
        var (classes, profiles, cascade) = Stores();
        var modified = AddClass(classes, "Modified");
        profiles.Upsert(new DriverProfile { Transponder = "1", DriverName = "Jo", ClassName = "Modified", Classes = new List<string> { "Mod" } });
        profiles.Upsert(new DriverProfile { Transponder = "2", DriverName = "Kim", ClassName = "Modified" });

        var undo = new UndoService();
        RenameInEditor(undo, classes, cascade, modified, "Mod");
        RenameInEditor(undo, classes, cascade, classes.Get(modified.Id)!, "Super");

        Assert.Equal(2, undo.UndoSteps(2).Count);

        Assert.Equal("Modified", classes.Get(modified.Id)!.Name);

        var reloaded = ProfileStore.Load();
        Assert.Equal(new[] { "Modified", "Mod" }, reloaded.Get("1")!.AllClasses);
        Assert.Equal(new[] { "Modified" }, reloaded.Get("2")!.AllClasses);
    }

    // --- profiles-5: a quick photo change's undo -----------------------------------------

    [Fact]
    public void A_photo_undo_finds_nobody_once_the_driver_is_deleted_and_their_transponder_given_to_someone_else()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", PhotoFileName = "sam.jpg" }, out _));
        var sam = store.Get("8467BE")!;

        store.Delete("8467BE");
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Alex", PhotoFileName = "alex.jpg" }, out _));

        Assert.Null(store.CurrentVersionOf(sam, "8467BE"));
        Assert.Equal("alex.jpg", store.Get("8467BE")!.PhotoFileName);
    }

    [Fact]
    public void A_photo_undo_still_finds_the_driver_after_an_edit_or_after_they_are_brought_back()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Sam" }, out _));
        var sam = store.Get("8467BE")!;

        // The edit form saves a new object for the same driver.
        var edited = new DriverProfile { Transponder = "8467BE", DriverName = "Sam", RaceNumber = "9" };
        store.Upsert(edited);
        Assert.Same(edited, store.CurrentVersionOf(sam, "8467be"));

        store.Delete("8467BE");
        Assert.Null(store.CurrentVersionOf(sam, "8467BE"));

        Assert.Empty(store.RestoreDeleted(new[] { edited }));
        Assert.Same(edited, store.CurrentVersionOf(sam, "8467BE"));
    }

    // --- helpers ------------------------------------------------------------------------

    private static (RaceClassStore Classes, ProfileStore Profiles, ClassRenameCascade Cascade) Stores()
    {
        var profiles = ProfileStore.Load();
        return (RaceClassStore.Load(), profiles, new ClassRenameCascade(profiles, RoundDefinitionStore.Load(), SeasonStore.Load()));
    }

    private static RaceClass AddClass(RaceClassStore classes, string name)
    {
        var raceClass = new RaceClass { Name = name };
        classes.Upsert(raceClass);
        return raceClass;
    }

    /// <summary>A rename as the Classes page's Edit makes it (RaceClassesControl.EditSelected), with its undo step.</summary>
    private static void RenameInEditor(UndoService undo, RaceClassStore classes, ClassRenameCascade cascade, RaceClass live, string newName)
    {
        var oldName = live.Name;
        var before = JsonSerializer.Deserialize<RaceClass>(JsonSerializer.Serialize(live));

        // The dialog writes onto the stored class itself.
        live.Name = newName;
        var afterJson = JsonSerializer.Serialize(live);
        ClassRenameReceipt? receipt = null;

        undo.Capture($"Rename class '{oldName}' to '{newName}'",
            () => classes.UndoRename(live.Id, oldName, before, afterJson, cascade, receipt));

        receipt = cascade.Rename(oldName, newName);
        classes.Upsert(live);
    }

    private static void AssertNamesUnique(RaceClassStore classes) =>
        Assert.All(classes.All.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase), group => Assert.Single(group));
}
