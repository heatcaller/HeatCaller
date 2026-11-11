namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16, rental-transponders.json holding the literal "null".
///
/// WHY. It parsed to nothing without throwing, loaded as an empty Rentals
/// page with LoadedUnreadable left false, and the first change saved over
/// every club unit, deposit and loan history. It must now hold writes.
/// </summary>
public sealed class BugHunt16_RentalTransponderStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_rentals_file_holding_literal_null_is_not_saved_over()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("rental-transponders.json"), "null");

        var store = RentalTransponderStore.Load();
        Assert.True(store.LoadedUnreadable);

        store.Upsert(new RentalTransponder { Transponder = "9" });

        Assert.Equal("null", File.ReadAllText(_data.PathOf("rental-transponders.json")));
    }

    [Fact]
    public void No_rentals_file_is_a_new_install_and_saves()
    {
        var store = RentalTransponderStore.Load();
        Assert.False(store.LoadedUnreadable);

        store.Upsert(new RentalTransponder { Transponder = "9" });

        Assert.Single(RentalTransponderStore.Load().All);
    }
}
