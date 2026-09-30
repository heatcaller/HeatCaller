namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The Rentals page's windows (RentalTransponderForms.cs): Check Out,
    /// with forty drivers on file to pick from - the longest names in the
    /// drop-down - and with nobody on file, an empty drop-down; Add Rental
    /// Unit, and the same window editing a retired unit with every field
    /// long; and the question Check In... asks (RentalPromptForm), for the
    /// longest name and a long deposit to give back.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> RentalWindows() => new (string, Func<Form>)[]
    {
        ("Check Out", () => RentalsCheckOutWindow(driversOnFile: true)),
        ("Check Out (nobody on file)", () => RentalsCheckOutWindow(driversOnFile: false)),
        ("Add Rental Unit", () => new RentalUnitEditForm(null)),
        ("Edit Rental Unit (every field long)", () => new RentalUnitEditForm(RentalsLongUnit())),
        ("Check in", () => RentalsCheckInWindow()),
    };

    /// <summary>
    /// Check Out for a unit on the shelf, over the drivers in the test's
    /// data folder. With drivers on file: forty of them (Drivers), the ones
    /// not there already added - a window built earlier in the same folder
    /// may have put some there with details of its own, which are left as
    /// they are. With nobody on file: every driver taken off while the
    /// window is built - it reads them only then - and put back straight
    /// after, so the windows built after it find the folder as it was.
    /// </summary>
    private static RentalCheckOutForm RentalsCheckOutWindow(bool driversOnFile)
    {
        var unit = new RentalTransponder { Transponder = "5000041", AssetTag = "R4" };
        var profiles = ProfileStore.Load();

        if (driversOnFile)
        {
            var missing = Drivers(40).Where(driver => profiles.Get(driver.Transponder) is null).ToList();

            if (missing.Count > 0)
            {
                profiles.UpsertMany(missing);
            }

            return new RentalCheckOutForm(unit, profiles);
        }

        // Everyone taken off in one save and put back as they were - the
        // store's own undo of a delete, which neither stamps a first-seen
        // date nor reports new cars.
        var everyone = profiles.All.ToList();

        profiles.PutBack(Array.Empty<DriverProfile>(), everyone.Select(driver => driver.Transponder).ToList());

        try
        {
            return new RentalCheckOutForm(unit, profiles);
        }
        finally
        {
            if (everyone.Count > 0)
            {
                profiles.RestoreDeleted(everyone);
            }
        }
    }

    /// <summary>
    /// A retired unit with every field as long as a club would make it: a
    /// long ID, an asset tag that says where it lives, and notes running to
    /// more lines than the box shows.
    /// </summary>
    private static RentalTransponder RentalsLongUnit() => new()
    {
        Transponder = "4005480123456",
        AssetTag = "R14 - blue sticker, spare charger in the tin",
        Notes = "Battery replaced 12 September 2026.\r\n"
            + "The clip on the left-hand side is cracked - tape it before it goes out.\r\n"
            + "Lent to the Juniors most weeks; check it is charged before the first heat.\r\n"
            + "Retired until the replacement clip arrives from the club's supplier.",
        Retired = true,
    };

    /// <summary>
    /// The question Check In... asks, worded as the Rentals page words it
    /// (RentalTransponderControl.CheckInSelected): the longest name there
    /// is, and a deposit to give back long enough to wrap.
    /// </summary>
    private static RentalPromptForm RentalsCheckInWindow()
    {
        var unit = RentalsLongUnit();
        var holder = Drivers(12)[11].DriverName;
        const string deposit = "Car key (silver estate, parked by the side door) and a £20 note";

        var prompt = $"Returning {unit.DisplayName} from {holder}.\r\n\r\n"
            + $"Deposit to give back: {deposit}\r\n\r\n"
            + "Any notes on its condition?";

        return new RentalPromptForm("Check in", prompt, string.Empty);
    }
}
