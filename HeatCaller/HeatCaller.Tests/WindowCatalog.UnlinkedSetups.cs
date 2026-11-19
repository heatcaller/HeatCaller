namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The setups that belong to no car (UnlinkedSetupsForm, titled Setups
    /// Without a Car), as the Garage page opens it (18 Nov 2026, dialog
    /// jazz-up, eighth batch): a season of setups logged before setups
    /// belonged to cars - more than the list shows, some with long driver,
    /// car and track names and one that names no car - and with every setup
    /// already linked. The setups are taken away again once it is built: it
    /// reads them as it is.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> UnlinkedSetupsWindows() => new (string, Func<Form>)[]
    {
        ("Setups Without a Car", () => UnlinkedSetupsWindow(count: 24)),
        ("Setups Without a Car (none left)", () => UnlinkedSetupsWindow(count: 0)),
    };

    private static UnlinkedSetupsForm UnlinkedSetupsWindow(int count)
    {
        var profiles = GarageDriversOnFile();
        var drivers = Drivers(40);
        var log = SetupLogStore.Load();
        var cars = CarStore.Load();

        string[] models = { "The blue Audi", "Kyosho Mini-Z MR-03 EVO Sports - the red one with the long arms", string.Empty, "Night Shift" };
        string[] tracks = { "Tuesday layout", "Riverside Autumn Championship - long infield", string.Empty };

        var added = Enumerable.Range(0, count).Select(i => new SetupEntry
        {
            RecordedAtUtc = new DateTime(2026, 3, 1, 19, 0, 0, DateTimeKind.Utc).AddDays(i * 7),
            DriverName = drivers[i % drivers.Count].DriverName,
            CarModel = models[i % models.Length],
            TrackLayoutName = tracks[i % tracks.Length],
        }).ToList();

        try
        {
            foreach (var entry in added)
            {
                log.Upsert(entry);
            }

            return new UnlinkedSetupsForm(log, cars, profiles, new UndoService());
        }
        finally
        {
            foreach (var entry in added)
            {
                log.Delete(entry.Id);
            }
        }
    }
}
