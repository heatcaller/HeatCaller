namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (2026-11-05), batteries and transponders.
///
/// WHY. A hand-edited or restored battery-packs.json with a pack saying
/// "Notes": null loaded as it was, and Driver Hub's Batteries tab and
/// Garage's Club Batteries both threw on pack.Notes.Replace. The store now
/// repairs it on load and on Upsert, as RentalTransponderStore does since
/// bug hunt 10.
///
/// And a club rental unit (registered on the Rentals page, so with no
/// profile) that crossed the line was offered Belongs To and New Driver on
/// the unknown-transponder banner with nothing to say it was the club's -
/// one click made it one driver's for good. UnknownTransponders.RentalRefusal
/// now turns those two down for a registered unit and points at Club Rental.
/// </summary>
public sealed class BugHunt11_BatteriesTranspondersTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_pack_with_null_notes_label_and_chemistry_loads_repaired()
    {
        File.WriteAllText(
            _data.PathOf("battery-packs.json"),
            "{\"Packs\":[{\"Id\":\"p1\",\"Label\":null,\"Chemistry\":null,\"Notes\":null,\"OwnerTransponder\":\"\"}]}");

        var store = BatteryPackStore.Load();
        var pack = Assert.Single(store.All);

        Assert.False(store.LoadedUnreadable);
        Assert.Equal(string.Empty, pack.Notes);
        Assert.Equal(string.Empty, pack.Label);
        Assert.Equal("NiMH", pack.Chemistry);
    }

    [Fact]
    public void Club_Batteries_opens_on_a_club_pack_with_null_notes()
    {
        File.WriteAllText(
            _data.PathOf("battery-packs.json"),
            "{\"Packs\":[{\"Id\":\"p1\",\"Label\":\"3\",\"Notes\":null,\"OwnerTransponder\":\"\"}]}");

        var store = BatteryPackStore.Load();

        // Threw NullReferenceException in Fill, called from the constructor.
        using var form = new ClubBatteriesForm(store, ProfileStore.Load(), new UndoService());
    }

    [Fact]
    public void A_clean_pack_loads_unchanged()
    {
        File.WriteAllText(
            _data.PathOf("battery-packs.json"),
            "{\"Packs\":[{\"Id\":\"p1\",\"Label\":\"Blue tape\",\"Chemistry\":\"LiPo\",\"Notes\":\"puffy\",\"OwnerTransponder\":\" 100 \"}]}");

        var pack = Assert.Single(BatteryPackStore.Load().All);

        Assert.Equal("Blue tape", pack.Label);
        Assert.Equal("LiPo", pack.Chemistry);
        Assert.Equal("puffy", pack.Notes);
        Assert.Equal("100", pack.OwnerTransponder);
    }

    [Fact]
    public void Upsert_repairs_a_null_notes_too()
    {
        var store = BatteryPackStore.Load();
        store.Upsert(new BatteryPack { Id = "p2", Label = "A2", Notes = null!, Chemistry = null!, });

        var pack = store.Get("p2")!;
        Assert.Equal(string.Empty, pack.Notes);
        Assert.Equal("NiMH", pack.Chemistry);
    }

    private static RentalTransponder Unit(string holder = "", bool retired = false) => new()
    {
        Transponder = "1234567",
        AssetTag = "R4",
        Retired = retired,
        CurrentHolderName = holder,
        CheckedOutUtc = holder.Length > 0 ? DateTime.UtcNow : null,
    };

    [Theory]
    [InlineData(UnknownTransponders.Choice.BelongsTo)]
    [InlineData(UnknownTransponders.Choice.NewDriver)]
    public void A_club_rental_out_with_a_driver_is_not_given_to_them(UnknownTransponders.Choice choice)
    {
        var text = UnknownTransponders.RentalRefusal(Unit("Sam"), choice);

        Assert.NotNull(text);
        Assert.Contains("R4", text);
        Assert.Contains("out with Sam", text);
        Assert.Contains("Club Rental", text);
    }

    [Fact]
    public void A_club_rental_on_the_shelf_is_refused_too_without_a_holder()
    {
        var text = UnknownTransponders.RentalRefusal(Unit(), UnknownTransponders.Choice.NewDriver);

        Assert.NotNull(text);
        Assert.DoesNotContain("out with", text);
    }

    [Fact]
    public void Club_Rental_an_unregistered_id_and_a_retired_unit_go_ahead()
    {
        Assert.Null(UnknownTransponders.RentalRefusal(Unit("Sam"), UnknownTransponders.Choice.ClubRental));
        Assert.Null(UnknownTransponders.RentalRefusal(null, UnknownTransponders.Choice.BelongsTo));
        Assert.Null(UnknownTransponders.RentalRefusal(Unit(retired: true), UnknownTransponders.Choice.BelongsTo));
    }
}
