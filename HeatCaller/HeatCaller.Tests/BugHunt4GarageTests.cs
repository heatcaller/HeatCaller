namespace HeatCaller.Tests;

/// <summary>
/// Cars and transponders changing hands, and cars retired: a car transfer
/// asks before a transponder going with it takes races already run along
/// (it used to move them without a word), a transponder fitted to a car
/// since retired no longer names that car on the board, and neither
/// Transfer window promises that races already run stay with the old owner.
/// </summary>
public sealed class BugHunt4GarageTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SessionRecord Race(string label, DateTime startedUtc, params string[] transponders) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = label,
        StartedAtUtc = startedUtc,
        Results = transponders.Select((t, i) => new SessionResultEntry { Position = i + 1, Transponder = t, DriverName = "D" + t }).ToList(),
    };

    /// <summary>Maya (100) with Spare and Red, her spare transponder 200 fitted to Red; Dev (500) with no car.</summary>
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

            Cars.Upsert(new Car { Id = "spare", Name = "Spare", OwnerTransponder = "100", AddedUtc = DateTime.UtcNow.AddMinutes(-10) });
            Cars.Upsert(new Car { Id = "red", Name = "Red", OwnerTransponder = "100", AddedUtc = DateTime.UtcNow });

            var withCars = ProfileStore.Snapshot(Profiles.Get("100")!);
            withCars.CarModel = "Red";
            withCars.Cars = new() { "Spare", "Red" };
            Profiles.Upsert(withCars);
        }

        public void Retire(string carId, bool retired = true)
        {
            var car = Cars.Get(carId)!.CloneForUndo();
            car.Retired = retired;
            Cars.Upsert(car);
        }
    }

    // ------------------------------------------------------------------
    //  garage-1: a car transfer asks before its transponders take races along
    // ------------------------------------------------------------------

    [Fact]
    public void A_car_going_with_a_spare_that_raced_as_its_own_car_names_those_races_and_asks()
    {
        var now = DateTime.UtcNow;

        // Heat 1: 200 merged into Maya's main 100. Heat 2: both her cars out,
        // so 200 had a row of its own - which follows whoever owns 200 now.
        var sessions = new[]
        {
            Race("Heat 1", now.AddHours(-2), "100", "500"),
            Race("Heat 2", now.AddHours(-1), "100", "200", "500"),
        };

        var ask = CarTransferring.AskFor(new CarTransferring.Plan("red", "500", new[] { "200" }), "Red", "Maya", "Dev", sessions);

        Assert.NotNull(ask);
        Assert.Contains("  Heat 2 - ", ask);
        Assert.DoesNotContain("  Heat 1 - ", ask);
        Assert.Contains("Once 200 is Dev's, qualifying and the mains will count it for Dev, not Maya.", ask);
        Assert.EndsWith("Transfer Red anyway?", ask);
        Assert.DoesNotContain("Transfer it anyway?", ask);
    }

    [Fact]
    public void Nothing_is_asked_when_no_transponder_goes_none_raced_as_its_own_car_or_it_goes_to_the_club()
    {
        var sessions = new[] { Race("Heat 2", DateTime.UtcNow, "100", "200", "500") };
        var merged = new[] { Race("Heat 1", DateTime.UtcNow, "100", "500") };

        Assert.Null(CarTransferring.AskFor(new CarTransferring.Plan("red", "500", Array.Empty<string>()), "Red", "Maya", "Dev", sessions));
        Assert.Null(CarTransferring.AskFor(new CarTransferring.Plan("red", "500", new[] { "200" }), "Red", "Maya", "Dev", merged));

        // The club keeps no transponders, so none goes with it (Apply).
        Assert.Null(CarTransferring.AskFor(new CarTransferring.Plan("red", string.Empty, new[] { "200" }), "Red", "Maya", "the club", sessions));
    }

    [Fact]
    public void Two_transponders_going_with_the_car_are_asked_about_in_one_question()
    {
        var now = DateTime.UtcNow;
        var sessions = new[]
        {
            Race("Heat 2", now.AddHours(-2), "100", "200"),
            Race("Heat 3", now.AddHours(-1), "100", "300"),
        };

        var ask = CarTransferring.AskFor(new CarTransferring.Plan("red", "500", new[] { "200", "300", " 200 " }), "Red", "Maya", "Dev", sessions)!;

        Assert.Contains("Once 200 is Dev's", ask);
        Assert.Contains("Once 300 is Dev's", ask);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ask, "Once 200 is"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ask, "anyway\\?"));
    }

    /// <summary>
    /// As for a transponder on its own (BugHuntPointsTests): leaving the
    /// history out has to be a build error, or the question is never asked.
    /// </summary>
    [Fact]
    public void Transferring_a_car_cannot_be_called_without_saying_where_the_race_history_is()
    {
        var run = typeof(CarTransferring).GetMethod(nameof(CarTransferring.Run))!;
        var history = Assert.Single(run.GetParameters(), p => p.ParameterType == typeof(SessionHistoryStore));
        Assert.False(history.HasDefaultValue);
    }

    [Fact]
    public void The_car_transfer_window_only_promises_races_stay_when_no_transponder_goes_with_it()
    {
        Assert.Equal(
            "Red becomes Dev's. Its photos, notes and setups go with it. Races already run stay with whoever ran them.",
            CarTransferForm.OutcomeText("Red", "Dev", 0));

        var one = CarTransferForm.OutcomeText("Red", "Dev", 1);
        Assert.Contains(", and so does its transponder.", one);
        Assert.Contains("timed as a car of its own", one);
        Assert.Contains("count for Dev", one);
        Assert.DoesNotContain("stay with whoever ran them.", one);

        Assert.Contains("one of those transponders was timed as a car of its own", CarTransferForm.OutcomeText("Red", "Dev", 2));
        Assert.EndsWith("Races already run stay with whoever ran them.", CarTransferForm.OutcomeText("Red", null, 0));
    }

    // ------------------------------------------------------------------
    //  garage-2: a retired car is no longer the car on the board
    // ------------------------------------------------------------------

    [Fact]
    public void A_transponder_fitted_to_a_car_since_retired_is_announced_in_the_active_car_instead()
    {
        var w = new World();
        var race = new RaceControlSession { AlternateTransponderOwner = w.Profiles.PrimaryForAlternate };
        w.Profiles.RaceCarName = row => GarageSync.FittedCar(race.HeardAs(row), w.Profiles, w.Cars)?.DisplayName;

        // Red breaks and is retired; her active car moves on to Spare (in the
        // app SyncNames does that), and 200 still says it's fitted to Red.
        w.Retire("red");
        var maya = ProfileStore.Snapshot(w.Profiles.Get("100")!);
        maya.CarModel = "Spare";
        w.Profiles.Upsert(maya);

        Assert.Null(GarageSync.FittedCar("200", w.Profiles, w.Cars));

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = "200", DeviceTimeMs = 101_000, SignalHits = 60, SignalStrength = 247 });

        Assert.Equal("200", race.HeardAs("100"));
        Assert.Equal("Spare", w.Profiles.CarModelFor("100"));

        // Put Back: the fitting was never cleared, so Red is the car again.
        w.Retire("red", retired: false);
        Assert.Equal("Red", GarageSync.FittedCar("200", w.Profiles, w.Cars)?.DisplayName);
        Assert.Equal("Red", w.Profiles.CarModelFor("100"));
    }

    [Fact]
    public void A_retired_car_transferred_to_a_driver_with_no_car_does_not_become_their_active_car()
    {
        var w = new World();
        w.Retire("red");

        Assert.Null(CarTransferring.Apply(new CarTransferring.Plan("red", "500", new[] { "200" }), w.Cars, w.Profiles, w.Undo));

        var dev = w.Profiles.Get("500")!;
        Assert.Equal("500", w.Cars.Get("red")!.OwnerTransponder);
        Assert.Equal(string.Empty, dev.CarModel ?? string.Empty);
        Assert.Null(GarageSync.FittedCar("200", w.Profiles, w.Cars));

        Assert.NotNull(w.Undo.Undo());
        Assert.Equal("100", w.Cars.Get("red")!.OwnerTransponder);
        Assert.Equal(string.Empty, w.Profiles.Get("500")!.CarModel ?? string.Empty);
        Assert.Equal("red", w.Profiles.Get("100")!.TransponderEntry("200")!.CarId);
    }

    // ------------------------------------------------------------------
    //  garage-3: the Transfer Transponder window tells the truth
    // ------------------------------------------------------------------

    [Fact]
    public void The_transfer_transponder_window_says_races_where_it_raced_as_its_own_car_count_for_the_new_owner()
    {
        var transfer = BelongsToForm.OutcomeText("200", transfer: true, "Dev", string.Empty);

        Assert.DoesNotContain("stay with whoever ran them", transfer);
        Assert.StartsWith("200 becomes Dev's, and their laps on it count for them.", transfer);
        Assert.Contains("timed as a car of its own", transfer);
        Assert.EndsWith(" 300 is retired.", BelongsToForm.OutcomeText("200", transfer: true, "Dev", "300"));

        // Giving a transponder nobody has is unchanged.
        Assert.Equal("Laps timed on 200 will count for Dev. 300 is retired.", BelongsToForm.OutcomeText("200", transfer: false, "Dev", "300"));
    }
}
