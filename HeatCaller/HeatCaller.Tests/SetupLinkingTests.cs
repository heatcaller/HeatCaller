namespace HeatCaller.Tests;

/// <summary>
/// Setups logged before setups belonged to cars, linked to the car they
/// were for (SetupLinking): only where that is certain, once, and the rest
/// left to be done by hand.
/// </summary>
public sealed class SetupLinkingTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public CarStore Cars { get; } = CarStore.Load();

        public SetupLogStore Setups { get; } = SetupLogStore.Load();

        public World()
        {
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Maya" }, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "500", DriverName = "Dev" }, out _));

            Cars.Upsert(new Car { Id = "blue", Name = "Blue Bird", Bodyshell = "GT3", OwnerTransponder = "100" });
            Cars.Upsert(new Car { Id = "night", Name = "Night Shift", OwnerTransponder = "100" });
            Cars.Upsert(new Car { Id = "dev", Name = "Blue Bird", OwnerTransponder = "500" });
        }

        public SetupEntry Log(string driver, string car)
        {
            var entry = new SetupEntry { DriverName = driver, CarModel = car };
            Setups.Upsert(entry);
            return entry;
        }
    }

    [Fact]
    public void A_setup_is_linked_only_when_one_car_of_that_drivers_fits()
    {
        var w = new World();

        // Her car by name, by shell and in other capitals; Dev's by name.
        Assert.Equal("blue", SetupLinking.CertainCarFor(w.Log("Maya", "Blue Bird"), w.Profiles, w.Cars)?.Id);
        Assert.Equal("blue", SetupLinking.CertainCarFor(w.Log("maya", "gt3"), w.Profiles, w.Cars)?.Id);
        Assert.Equal("dev", SetupLinking.CertainCarFor(w.Log("Dev", "Blue Bird"), w.Profiles, w.Cars)?.Id);

        // Nothing typed, a car she doesn't have, a driver not on file: asked.
        Assert.Null(SetupLinking.CertainCarFor(w.Log("Maya", ""), w.Profiles, w.Cars));
        Assert.Null(SetupLinking.CertainCarFor(w.Log("Maya", "MR-03"), w.Profiles, w.Cars));
        Assert.Null(SetupLinking.CertainCarFor(w.Log("Sam", "Blue Bird"), w.Profiles, w.Cars));
    }

    [Fact]
    public void Two_of_her_cars_with_the_name_is_not_certain()
    {
        var w = new World();
        w.Cars.Upsert(new Car { Id = "blue2", Name = "Blue Bird", OwnerTransponder = "100" });

        Assert.Null(SetupLinking.CertainCarFor(w.Log("Maya", "Blue Bird"), w.Profiles, w.Cars));
    }

    [Fact]
    public void The_pass_runs_once_and_leaves_the_rest_unlinked()
    {
        var w = new World();
        var sure = w.Log("Maya", "Night Shift");
        var unsure = w.Log("Maya", "MR-03");
        var settings = new AppSettings();

        var (linked, left) = SetupLinking.LinkOnce(settings, w.Setups, w.Profiles, w.Cars);

        Assert.Equal(1, linked);
        Assert.Equal(1, left);
        Assert.Equal("night", w.Setups.Get(sure.Id)!.CarId);
        Assert.Equal("Night Shift", w.Setups.Get(sure.Id)!.CarModel);
        Assert.True(string.IsNullOrEmpty(w.Setups.Get(unsure.Id)!.CarId));
        Assert.True(settings.HasLinkedSetupsToCars);

        // Linked by hand or not, a second run changes nothing.
        w.Log("Maya", "Blue Bird");
        Assert.Equal((0, 0), SetupLinking.LinkOnce(settings, w.Setups, w.Profiles, w.Cars));
    }

    [Fact]
    public void A_setup_whose_car_was_deleted_counts_as_without_a_car()
    {
        var w = new World();
        var entry = w.Log("Maya", "Night Shift");
        entry.CarId = "night";
        w.Setups.Upsert(entry);

        Assert.DoesNotContain(SetupLinking.Unlinked(w.Setups, w.Cars), e => e.Id == entry.Id);

        w.Cars.Delete("night");
        Assert.Contains(SetupLinking.Unlinked(w.Setups, w.Cars), e => e.Id == entry.Id);
    }
}
