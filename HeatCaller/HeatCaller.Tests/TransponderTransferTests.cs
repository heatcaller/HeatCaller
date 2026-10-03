namespace HeatCaller.Tests;

/// <summary>
/// A transponder changing hands between two drivers for good
/// (TransponderTransferring): the extra ones only, never a driver's main
/// one; fitted to the new owner's car or moving between theirs, replacing
/// one of theirs if asked; and one Undo for both drivers.
/// </summary>
public sealed class TransponderTransferTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public CarStore Cars { get; } = CarStore.Load();

        public UndoService Undo { get; } = new();

        public World()
        {
            Cars.Upsert(new Car { Id = "red", Name = "Red", OwnerTransponder = "100" });
            Cars.Upsert(new Car { Id = "blue", Name = "Blue", OwnerTransponder = "500" });

            var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
            maya.Transponders.Add(new OwnedTransponder { Id = "200", CarId = "red" });
            Assert.True(Profiles.TryAdd(maya, out _));

            var dev = new DriverProfile { Transponder = "500", DriverName = "Dev" };
            dev.Transponders.Add(new OwnedTransponder { Id = "600" });
            Assert.True(Profiles.TryAdd(dev, out _));
        }

        public string? Transfer(string id, string from, string to, string carId = "", string replaces = "") =>
            TransponderTransferring.Apply(new TransponderTransferring.Plan(id, from, to, carId, replaces), Profiles, Cars, Undo);
    }

    [Fact]
    public void A_transponder_moves_to_the_new_owner_fitted_to_their_car_and_undo_puts_it_back()
    {
        var w = new World();

        Assert.Null(w.Transfer("200", "100", "500", carId: "blue"));

        Assert.DoesNotContain("200", w.Profiles.Get("100")!.AllTransponderIds);
        Assert.Equal("blue", w.Profiles.Get("500")!.TransponderEntry("200")!.CarId);
        Assert.False(w.Profiles.Get("500")!.IsRetired("200"));
        Assert.Equal("500", w.Profiles.PrimaryForAlternate("200"));

        Assert.NotNull(w.Undo.Undo());

        Assert.Equal("red", w.Profiles.Get("100")!.TransponderEntry("200")!.CarId);
        Assert.Null(w.Profiles.Get("500")!.TransponderEntry("200"));
        Assert.Equal("100", w.Profiles.PrimaryForAlternate("200"));
    }

    [Fact]
    public void A_drivers_main_transponder_never_moves()
    {
        var w = new World();

        Assert.Contains("main transponder", w.Transfer("100", "100", "500"));
        Assert.Equal("Maya", w.Profiles.Get("100")!.DriverName);
        Assert.Null(w.Profiles.Get("500")!.TransponderEntry("100"));
        Assert.False(w.Undo.CanUndo);
    }

    [Fact]
    public void It_can_replace_one_of_theirs_and_is_never_fitted_to_a_car_that_is_not_theirs()
    {
        var w = new World();

        // Maya's own car named for Dev: it moves between his cars instead.
        Assert.Null(w.Transfer("200", "100", "500", carId: "red", replaces: "600"));

        var dev = w.Profiles.Get("500")!;
        Assert.Equal(string.Empty, dev.TransponderEntry("200")!.CarId);
        Assert.True(dev.IsRetired("600"));

        Assert.NotNull(w.Undo.Undo());
        Assert.False(w.Profiles.Get("500")!.IsRetired("600"));
    }

    [Fact]
    public void Undo_is_refused_once_either_drivers_transponders_have_changed_again()
    {
        var w = new World();
        Assert.Null(w.Transfer("200", "100", "500"));

        var dev = ProfileStore.Snapshot(w.Profiles.Get("500")!);
        dev.EntryToChange("200").CarId = "blue";
        w.Profiles.Upsert(dev);

        Assert.Null(w.Undo.Undo());
        Assert.Equal("500", w.Profiles.PrimaryForAlternate("200"));
    }

    [Fact]
    public void Nothing_moves_to_the_same_driver_or_one_no_longer_on_file()
    {
        var w = new World();

        Assert.NotNull(w.Transfer("200", "100", "100"));
        Assert.NotNull(w.Transfer("200", "100", "999"));
        Assert.NotNull(w.Transfer("777", "100", "500"));
        Assert.Equal("100", w.Profiles.PrimaryForAlternate("200"));
        Assert.False(w.Undo.CanUndo);
    }
}
