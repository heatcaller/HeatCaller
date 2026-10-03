namespace HeatCaller.Tests;

/// <summary>
/// The garage as the source of a driver's cars: their car names following
/// it (GarageSync.SyncNames), a transponder fitted to a car making that the
/// car on the board (FittedCar, HeardAs, CarModelFor), and a car changing
/// hands for good (CarTransferring) - and back again with Undo.
/// </summary>
public sealed class GarageOwnershipTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static Car AddCar(CarStore cars, string id, string name, string owner, int minutesAgo = 0, bool retired = false)
    {
        var car = new Car { Id = id, Name = name, OwnerTransponder = owner, Retired = retired, AddedUtc = DateTime.UtcNow.AddMinutes(-minutesAgo) };
        cars.Upsert(car);
        return car;
    }

    /// <summary>What the next change to the garage reports, as CarStore.Changed hands it to the app.</summary>
    private static List<CarChange> Watch(CarStore cars)
    {
        var seen = new List<CarChange>();
        cars.Changed += changes =>
        {
            seen.Clear();
            seen.AddRange(changes);
        };
        return seen;
    }

    [Fact]
    public void The_active_car_follows_a_rename()
    {
        var cars = CarStore.Load();
        AddCar(cars, "a", "Red", "100");
        var seen = Watch(cars);
        var maya = new DriverProfile { Transponder = "100", DriverName = "Maya", CarModel = "Red", Cars = new() { "Red" } };

        var renamed = cars.Get("a")!.CloneForUndo();
        renamed.Name = "Blue";
        cars.Upsert(renamed);

        var change = Assert.Single(seen);
        Assert.Equal("Red", change.Before!.Name);
        Assert.Equal("Blue", change.After!.Name);

        var synced = GarageSync.SyncNames(maya, cars, seen)!;
        Assert.Equal("Blue", synced.CarModel);
        Assert.Equal(new[] { "Blue" }, synced.Cars);

        // And once in step, nothing to save - and nothing to save for a
        // change that is nothing to do with them either.
        Assert.Null(GarageSync.SyncNames(synced, cars, seen));
        Assert.Null(GarageSync.SyncNames(synced, cars, Array.Empty<CarChange>()));
    }

    [Fact]
    public void A_change_to_somebody_elses_car_leaves_a_driver_alone()
    {
        var cars = CarStore.Load();
        AddCar(cars, "a", "Red", "100");
        var seen = Watch(cars);

        // A name typed with no car behind it, and one of a car since retired.
        var maya = new DriverProfile { Transponder = "100", CarModel = "Red", Cars = new() { "Red", "Typed", "Old one" } };

        AddCar(cars, "d", "Dev's", "500");
        Assert.Single(seen);
        Assert.Null(GarageSync.SyncNames(maya, cars, seen));
    }

    [Fact]
    public void A_car_picked_on_the_profile_is_left_alone()
    {
        var cars = CarStore.Load();
        AddCar(cars, "a", "Red", "100", minutesAgo: 10);
        AddCar(cars, "b", "Green", "100");

        // Same cars, another order and other capitals: nothing written.
        var maya = new DriverProfile { Transponder = "100", CarModel = "Green", Cars = new() { "green", "Red" } };
        Assert.Null(GarageSync.SyncNames(maya, cars, Array.Empty<CarChange>()));
    }

    [Fact]
    public void An_active_car_retired_hands_over_to_their_next_car_or_none()
    {
        var cars = CarStore.Load();
        AddCar(cars, "a", "Red", "100", minutesAgo: 10);
        AddCar(cars, "b", "Green", "100");
        var seen = Watch(cars);
        var maya = new DriverProfile { Transponder = "100", CarModel = "Red", Cars = new() { "Red", "Green" } };

        var retired = cars.Get("a")!.CloneForUndo();
        retired.Retired = true;
        cars.Upsert(retired);

        var synced = GarageSync.SyncNames(maya, cars, seen)!;
        Assert.Equal("Green", synced.CarModel);
        Assert.Equal(new[] { "Green" }, synced.Cars);

        cars.Delete("b");
        var none = GarageSync.SyncNames(synced, cars, seen)!;
        Assert.Equal(string.Empty, none.CarModel);
        Assert.Empty(none.Cars);
    }

    [Fact]
    public void A_typed_car_with_no_garage_car_behind_it_is_left_as_typed()
    {
        var cars = CarStore.Load();
        var maya = new DriverProfile { Transponder = "100", CarModel = "MR-03", Cars = new() { "MR-03" } };

        Assert.Null(GarageSync.SyncNames(maya, cars, Array.Empty<CarChange>()));
    }

    [Fact]
    public void A_car_already_retired_is_not_taken_off_anyone_at_startup()
    {
        var cars = CarStore.Load();
        AddCar(cars, "a", "Red", "100", retired: true);
        AddCar(cars, "b", "Green", "100");
        var maya = new DriverProfile { Transponder = "100", CarModel = "Red", Cars = new() { "Red", "Green" } };

        // Nothing has just changed: what they are announced in stays.
        Assert.Null(GarageSync.SyncNames(maya, cars, Array.Empty<CarChange>()));
    }

    [Fact]
    public void A_car_owned_under_another_of_their_transponders_gets_no_twin()
    {
        var cars = CarStore.Load();
        AddCar(cars, "a", "Spare", "200");
        var maya = new DriverProfile { Transponder = "100" };
        maya.Transponders.Add(new OwnedTransponder { Id = "200" });

        Assert.Equal(0, GarageSync.AddCars(maya, new[] { "Spare" }, cars));
        Assert.Single(cars.All);
    }

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public CarStore Cars { get; } = CarStore.Load();

        public UndoService Undo { get; } = new();

        public World()
        {
            var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
            maya.Transponders.Add(new OwnedTransponder { Id = "200", CarId = "red" });
            Assert.True(Profiles.TryAdd(maya, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "500", DriverName = "Dev" }, out _));

            AddCar(Cars, "spare", "Spare", "100", minutesAgo: 10);
            AddCar(Cars, "red", "Red", "100");

            var withCars = ProfileStore.Snapshot(Profiles.Get("100")!);
            withCars.CarModel = "Red";
            withCars.Cars = new() { "Spare", "Red" };
            Profiles.Upsert(withCars);
        }
    }

    [Fact]
    public void A_transfer_moves_the_car_and_the_transponders_that_go_with_it_and_undo_puts_them_back()
    {
        var w = new World();

        Assert.Null(CarTransferring.Apply(new CarTransferring.Plan("red", "500", new[] { "200" }), w.Cars, w.Profiles, w.Undo));

        var car = w.Cars.Get("red")!;
        Assert.Equal("500", car.OwnerTransponder);
        Assert.Equal("100", Assert.Single(car.Transfers).FromTransponder);

        var maya = w.Profiles.Get("100")!;
        Assert.DoesNotContain("200", maya.AllTransponderIds);
        Assert.Equal("Spare", maya.CarModel);
        Assert.Equal(new[] { "Spare" }, maya.Cars);

        var dev = w.Profiles.Get("500")!;
        Assert.Equal("red", dev.TransponderEntry("200")!.CarId);
        Assert.Equal("Red", dev.CarModel);
        Assert.Same(dev, w.Profiles.Get("200"));

        Assert.NotNull(w.Undo.Undo());

        Assert.Equal("100", w.Cars.Get("red")!.OwnerTransponder);
        Assert.Empty(w.Cars.Get("red")!.Transfers);
        Assert.Equal("Red", w.Profiles.Get("100")!.CarModel);
        Assert.Equal("red", w.Profiles.Get("100")!.TransponderEntry("200")!.CarId);
        Assert.Equal(string.Empty, w.Profiles.Get("500")!.CarModel);
        Assert.Equal("100", w.Profiles.PrimaryForAlternate("200"));
    }

    [Fact]
    public void A_transponder_left_behind_stays_with_the_old_owner_and_moves_between_their_cars()
    {
        var w = new World();

        Assert.Null(CarTransferring.Apply(new CarTransferring.Plan("red", "500", Array.Empty<string>()), w.Cars, w.Profiles, w.Undo));

        var entry = w.Profiles.Get("100")!.TransponderEntry("200")!;
        Assert.Equal(string.Empty, entry.CarId);
        Assert.Null(w.Profiles.Get("500")!.TransponderEntry("200"));
    }

    [Fact]
    public void To_the_club_no_transponder_goes_and_a_club_car_cant_go_to_the_club()
    {
        var w = new World();

        Assert.Null(CarTransferring.Apply(new CarTransferring.Plan("red", string.Empty, new[] { "200" }), w.Cars, w.Profiles, w.Undo));
        Assert.True(w.Cars.Get("red")!.IsClubCar);
        Assert.Contains("200", w.Profiles.Get("100")!.AllTransponderIds);

        Assert.NotNull(CarTransferring.Apply(new CarTransferring.Plan("red", string.Empty, Array.Empty<string>()), w.Cars, w.Profiles, w.Undo));
    }

    [Fact]
    public void Undo_is_refused_once_the_transponder_has_moved_on_again()
    {
        var w = new World();
        Assert.Null(CarTransferring.Apply(new CarTransferring.Plan("red", "500", new[] { "200" }), w.Cars, w.Profiles, w.Undo));

        var dev = ProfileStore.Snapshot(w.Profiles.Get("500")!);
        dev.Transponders.Clear();
        w.Profiles.Upsert(dev);

        Assert.Null(w.Undo.Undo());
        Assert.Equal("500", w.Cars.Get("red")!.OwnerTransponder);
    }

    [Fact]
    public void The_car_on_the_board_is_the_one_the_heard_transponder_is_fitted_to()
    {
        var w = new World();
        var race = new RaceControlSession { AlternateTransponderOwner = w.Profiles.PrimaryForAlternate };
        w.Profiles.RaceCarName = row => GarageSync.FittedCar(race.HeardAs(row), w.Profiles, w.Cars)?.DisplayName;

        // Maya's active car is Red, and 200 is fitted to it.
        var red = w.Profiles.Get("100")!;
        red.CarModel = "Spare";
        w.Profiles.Upsert(red);

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = "200", DeviceTimeMs = 101_000, SignalHits = 60, SignalStrength = 247 });

        // Filed under her main transponder, in the car 200 is fitted to.
        Assert.Equal("200", race.HeardAs("100"));
        Assert.Equal("Red", w.Profiles.CarModelFor("100"));

        // A driver whose transponder is fitted to nothing: their active car;
        // and nobody: nothing.
        var dev = w.Profiles.Get("500")!;
        dev.CarModel = "Yellow";
        w.Profiles.Upsert(dev);
        Assert.Equal("Yellow", w.Profiles.CarModelFor("500"));
        Assert.Equal(string.Empty, w.Profiles.CarModelFor("999"));
    }
}
