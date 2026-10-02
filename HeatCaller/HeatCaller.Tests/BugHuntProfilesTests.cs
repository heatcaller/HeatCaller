namespace HeatCaller.Tests;

/// <summary>
/// Fixes in the driver profile store.
///
/// The undo of an edit or an import (ProfileStore.PutBack) only puts a driver
/// back over a later version of that same driver - never over, or by
/// removing, somebody given the same transponder after that driver was
/// deleted.
///
/// A car given to a driver already on file by changing their stored profile
/// in place (the CSV import does) still reaches the Garage through CarsAdded.
/// </summary>
public sealed class BugHuntProfilesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- PutBack -------------------------------------------------------------------------

    [Fact]
    public void An_edit_undo_puts_the_old_details_back_over_the_same_driver()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", RaceNumber = "7" }, out _));

        var before = ProfileStore.Snapshot(store.Get("8467BE")!);
        store.Upsert(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", RaceNumber = "9" });

        Assert.Equal(1, store.PutBack(new[] { before }, Array.Empty<string>()));
        Assert.Equal("7", store.Get("8467BE")!.RaceNumber);
    }

    [Fact]
    public void Two_edit_undos_in_a_row_both_put_back()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", RaceNumber = "7" }, out _));

        var first = ProfileStore.Snapshot(store.Get("8467BE")!);
        store.Upsert(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", RaceNumber = "8" });

        var second = ProfileStore.Snapshot(store.Get("8467BE")!);
        store.Upsert(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", RaceNumber = "9" });

        Assert.Equal(1, store.PutBack(new[] { second }, Array.Empty<string>()));
        Assert.Equal(1, store.PutBack(new[] { first }, Array.Empty<string>()));
        Assert.Equal("7", store.Get("8467BE")!.RaceNumber);
    }

    [Fact]
    public void An_edit_undo_leaves_a_driver_added_on_the_transponder_since_alone()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Sam" }, out _));

        var before = ProfileStore.Snapshot(store.Get("8467BE")!);
        store.Upsert(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", RaceNumber = "9" });

        store.Delete("8467BE");
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Alex" }, out _));

        Assert.Equal(0, store.PutBack(new[] { before }, Array.Empty<string>()));
        Assert.Equal("Alex", store.Get("8467BE")!.DriverName);
    }

    [Fact]
    public void An_edit_undo_still_works_after_the_driver_is_deleted_and_restored()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", RaceNumber = "7" }, out _));

        var before = ProfileStore.Snapshot(store.Get("8467BE")!);
        store.Upsert(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", RaceNumber = "9" });

        var deleted = store.Get("8467BE")!;
        store.Delete("8467BE");
        Assert.Empty(store.RestoreDeleted(new[] { deleted }));

        Assert.Equal(1, store.PutBack(new[] { before }, Array.Empty<string>()));
        Assert.Equal("7", store.Get("8467BE")!.RaceNumber);
    }

    [Fact]
    public void An_import_undo_given_the_added_profiles_removes_only_those_drivers()
    {
        var store = ProfileStore.Load();
        var bea = new DriverProfile { Transponder = "T1", DriverName = "Bea" };
        var dot = new DriverProfile { Transponder = "T2", DriverName = "Dot" };
        store.UpsertMany(new[] { bea, dot });

        // Bea deleted and somebody else added on her transponder; Dot untouched.
        store.Delete("T1");
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "T1", DriverName = "Carl" }, out _));

        Assert.Equal(1, store.PutBack(Array.Empty<DriverProfile>(), new[] { bea, dot }));
        Assert.Equal("Carl", store.Get("T1")!.DriverName);
        Assert.Null(store.Get("T2"));
    }

    // --- CarsAdded -----------------------------------------------------------------------

    [Fact]
    public void A_car_set_on_the_stored_profile_in_place_is_still_reported_as_new()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", CarModel = "MR-03" }, out _));

        var reported = new List<string>();
        store.CarsAdded += (_, names) => reported.AddRange(names);

        // What the CSV import does: the live profile, changed, then saved.
        var live = store.Get("8467BE")!;
        live.CarModel = "MR-04";
        store.UpsertMany(new[] { live });

        Assert.Equal(new[] { "MR-04" }, reported);
    }

    [Fact]
    public void Saving_a_profile_over_itself_without_a_car_change_reports_nothing()
    {
        var store = ProfileStore.Load();
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "8467BE", DriverName = "Sam", CarModel = "MR-03" }, out _));

        var reported = new List<string>();
        store.CarsAdded += (_, names) => reported.AddRange(names);

        var live = store.Get("8467BE")!;
        live.RaceNumber = "12";
        store.Upsert(live);

        Assert.Empty(reported);
    }
}
