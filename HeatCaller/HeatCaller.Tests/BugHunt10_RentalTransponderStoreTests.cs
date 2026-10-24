namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10, nulls in rental-transponders.json.
///
/// WHY. A hand-edited or restored rentals file with a null unit, or a unit
/// saying "Notes": null or "History": null, loaded as it was and crashed the
/// Rentals page while MainForm built its pages - HeatCaller would not start.
/// Load now repairs it the way RivalryStore does.
/// </summary>
public sealed class BugHunt10_RentalTransponderStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_rentals_file_with_nulls_in_it_loads_repaired()
    {
        File.WriteAllText(
            _data.PathOf("rental-transponders.json"),
            "[null, {\"Transponder\":\"1\",\"Notes\":null,\"AssetTag\":null,\"CurrentHolderName\":null,\"DepositHeld\":null,\"History\":null},"
            + " {\"Transponder\":\"2\",\"History\":[null,{\"HolderName\":null,\"DepositHeld\":null,\"ReturnNotes\":null}]},"
            + " {\"Transponder\":null}]");

        var store = RentalTransponderStore.Load();
        var all = store.All;

        Assert.False(store.LoadedUnreadable);
        Assert.Equal(2, all.Count);

        var one = store.Get("1")!;
        Assert.Equal(string.Empty, one.Notes);
        Assert.Equal(string.Empty, one.AssetTag);
        Assert.Equal(string.Empty, one.CurrentHolderName);
        Assert.Equal(string.Empty, one.DepositHeld);
        Assert.Empty(one.History);

        var two = store.Get("2")!;
        var loan = Assert.Single(two.History);
        Assert.Equal(string.Empty, loan.HolderName);
        Assert.Equal(string.Empty, loan.DepositHeld);
        Assert.Equal(string.Empty, loan.ReturnNotes);
    }

    [Fact]
    public void A_clean_rentals_file_loads_unchanged()
    {
        File.WriteAllText(
            _data.PathOf("rental-transponders.json"),
            "[{\"Transponder\":\"7\",\"AssetTag\":\"R4\",\"Notes\":\"blue\",\"History\":[{\"HolderName\":\"Sam\",\"DepositHeld\":\"key\",\"ReturnNotes\":\"ok\"}]}]");

        var unit = Assert.Single(RentalTransponderStore.Load().All);

        Assert.Equal("R4", unit.AssetTag);
        Assert.Equal("blue", unit.Notes);
        Assert.Equal("Sam", Assert.Single(unit.History).HolderName);
    }
}
