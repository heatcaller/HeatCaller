namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Two windows that pick one thing from a list (18 Nov 2026, dialog
    /// jazz-up, third batch). Pick a driver (PickDriverForm), as Race
    /// Program's Late entry... opens it - a class's drivers, more than the
    /// list shows - and as a marshal post's swap opens it, with the owner of
    /// the post first and a long prompt. And which car a setup was for
    /// (PickCarForm), as the Garage's setups with no car open it: a driver
    /// with three cars, one of them named on the setup and one retired, and
    /// the rest of the garage; and a setup that names no car or driver.
    /// Neither is on LayoutAudit.ConvertedWindows, so their findings are in
    /// the report without failing the sweep. And the other classes a driver
    /// races (OtherClassesForm) at a club with no other class to pick.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> PickOneWindows() => new (string, Func<Form>)[]
    {
        ("Pick a Driver (late entry)", () => new PickDriverForm(
            "Late entry - Stock Buggy",
            "Pick the driver who's arrived. They go into the smallest heat of each round still to run, "
            + "and nobody else in the heats moves. Undo takes them out again.",
            "Add to Heats",
            Drivers(18).Select(driver => (driver.Transponder, driver.DriverName)))),
        ("Pick a Driver (cover a post)", () =>
        {
            var drivers = Drivers(12);
            var owner = drivers[3];

            return new PickDriverForm(
                $"Cover Corner 2 - Qualifier 1 Heat 2",
                $"Who covers {owner.DriverName}'s post (Corner 2) for Qualifier 1 Heat 2? Pick {owner.DriverName} back to undo a swap. "
                + "The marshal call, the phones and this sheet all follow it.",
                "Swap",
                drivers.Where(driver => driver != owner)
                    .Select(driver => (driver.Transponder, driver.DriverName))
                    .Prepend((owner.Transponder, owner.DriverName + " (their own post)")));
        }),
        ("Pick a Car", () => PickCarWindow(named: true)),
        ("Pick a Car (nothing named)", () => PickCarWindow(named: false)),

        // Choose... beside "Also races" at a club with one class, where the
        // line over the empty list says what fills it.
        ("Other Classes (none to pick)", () => new OtherClassesForm("Stock Buggy", new[] { "Stock Buggy" }, Array.Empty<string>())),
    };

    /// <summary>
    /// The car picker over the forty drivers and a garage of nine cars: three
    /// of the fourth driver's, one retired, one the club's and the rest
    /// other drivers'. The cars are taken away again once it is built - it
    /// reads them as it is.
    /// </summary>
    private static PickCarForm PickCarWindow(bool named)
    {
        var profiles = GarageDriversOnFile();
        var drivers = Drivers(40);
        var owner = drivers[3];

        var garage = new List<Car>
        {
            new() { Name = "The blue Audi", OwnerTransponder = owner.Transponder, Bodyshell = "Audi R8 LMS" },
            new() { Name = "Night Shift", OwnerTransponder = owner.Transponder, Bodyshell = "Porsche 911 GT3 RS" },
            new() { Name = "Old faithful", OwnerTransponder = owner.Transponder, Retired = true },
            new() { Name = "Club car 3 - the yellow Mini Cooper" },
        };

        garage.AddRange(Enumerable.Range(5, 5).Select(i => new Car
        {
            Name = i % 2 == 0 ? "Kyosho MR-03 EVO Sports" : $"Car {i}",
            OwnerTransponder = drivers[i * 3].Transponder,
        }));

        var entry = named
            ? new SetupEntry
            {
                RecordedAtUtc = new DateTime(2026, 6, 3, 19, 0, 0, DateTimeKind.Utc),
                DriverName = owner.DriverName,
                CarModel = "The blue Audi",
            }
            : new SetupEntry { RecordedAtUtc = new DateTime(2026, 6, 3, 19, 0, 0, DateTimeKind.Utc) };

        var cars = CarStore.Load();

        try
        {
            foreach (var car in garage)
            {
                cars.Upsert(car);
            }

            return new PickCarForm(entry, cars, profiles);
        }
        finally
        {
            foreach (var car in garage)
            {
                cars.Delete(car.Id);
            }
        }
    }
}
