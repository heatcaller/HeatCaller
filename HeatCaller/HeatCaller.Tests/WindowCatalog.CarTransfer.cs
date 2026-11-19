namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// A car's Transfer... from the Garage (CarTransferForm, titled
    /// "Transfer &lt;car&gt;"), over the catalog's forty drivers: a driver's
    /// car with three of their spare transponders fitted to it and their
    /// main one too, so every line the window can say is there; and a club
    /// car, which has no transponders to send with it. The owner's
    /// transponders are set on the store's copy of them only, never saved,
    /// so the windows built after it find the folder as it was.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> CarTransferWindows() => new (string, Func<Form>)[]
    {
        ("Transfer Car", () => CarTransferWindow(clubCar: false)),
        ("Transfer Car (a club car)", () => CarTransferWindow(clubCar: true)),
    };

    private static CarTransferForm CarTransferWindow(bool clubCar)
    {
        var profiles = GarageDriversOnFile();

        if (clubCar)
        {
            return new CarTransferForm(new Car { Name = "Club car 3 - the yellow Mini Cooper" }, profiles);
        }

        var car = GarageLongCar();
        var owner = profiles.Get(Drivers(40)[11].Transponder)!;
        car.OwnerTransponder = owner.Transponder;

        owner.Transponders = new List<OwnedTransponder>
        {
            new() { Id = owner.Transponder, CarId = car.Id },
            new() { Id = "8800101", CarId = car.Id },
            new() { Id = "8800102", CarId = car.Id },
            new() { Id = "8800103", CarId = car.Id },
        };

        return new CarTransferForm(car, profiles);
    }
}
