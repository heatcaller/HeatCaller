namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Points carried in (CarriedPointsForm), from Championship's Points
    /// Carried In: thirty drivers carrying points in, the longest names among
    /// them, and a championship nobody carries anything into yet. Racer
    /// Check-in PINs (RacerPinsForm), from Club Website's Show PINs...: forty
    /// made-up racers with long names, classes and addresses, and nobody with
    /// a PIN yet. And the backup password asked for at start-up after a
    /// backup is restored onto another PC (SecretRecovery.PasswordPromptForm),
    /// whose check is never run here.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> PointsAndPinsWindows() => new (string, Func<Form>)[]
    {
        ("Points carried in", () => PointsPinsCarriedPoints(drivers: 30)),
        ("Points carried in (none yet)", () => PointsPinsCarriedPoints(drivers: 0)),
        ("Racer Check-in PINs", () => PointsPinsRacerPins(racers: 40)),
        ("Racer Check-in PINs (nobody has one)", () => PointsPinsRacerPins(racers: 0)),
        ("Backup password", () => new SecretRecovery.PasswordPromptForm(_ => false)),
    };

    /// <summary>
    /// Points carried in, as Championship's Points Carried In opens it
    /// (SeasonsControl.EditCarriedPoints): <paramref name="drivers"/> drivers
    /// carrying points in - the longest names Drivers makes among them - and
    /// the names the standings know, offered as a row is typed. With none, a
    /// championship not named yet ("championship" in the title) with nobody
    /// carrying anything in.
    /// </summary>
    private static CarriedPointsForm PointsPinsCarriedPoints(int drivers)
    {
        var known = Drivers(Math.Max(drivers, 12));

        var entries = known
            .Take(drivers)
            .Select((driver, i) => new CarriedPointsEntry { DriverName = driver.DriverName, Points = 1_250 - i * 37 })
            .ToList();

        return new CarriedPointsForm(
            drivers > 0 ? "2026 Club Championship - Stock Buggy" : string.Empty,
            entries,
            known.Select(driver => driver.DriverName));
    }

    /// <summary>
    /// Racer Check-in PINs, as Club Website's Show PINs... opens it
    /// (RelaySettingsControl): <paramref name="racers"/> made-up racers, each
    /// with a made-up PIN - a third with an email address, a third with one
    /// only in their Contact box, a third with none, and every other one with
    /// an address already emailed - the longest names Drivers makes, a long
    /// class name and long addresses among them. With none, nobody has a PIN.
    ///
    /// The PINs are held only in the settings handed to the window, which
    /// are never saved, so they are written nowhere - not even to the test's
    /// data folder. The window only reads them. The racers go into the data
    /// folder, on transponders of their own so no other window's drivers are
    /// touched, and are taken out again when the window goes, so the windows
    /// built after it find the folder as they would have without it.
    /// </summary>
    private static RacerPinsForm PointsPinsRacerPins(int racers)
    {
        var settings = new AppSettings();
        var profiles = ProfileStore.Load();

        var added = Drivers(racers)
            .Select((driver, i) => new DriverProfile
            {
                Transponder = (7_700_000 + (i + 1) * 211).ToString(),
                DriverName = driver.DriverName,
                ClassName = i % 5 == 0 ? "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)" : "Stock Buggy",
                Email = i % 3 == 0 ? PointsPinsEmailFor(driver.DriverName) : string.Empty,
                Contact = i % 3 == 1 ? PointsPinsEmailFor(driver.DriverName) : string.Empty,
            })
            .Where(racer => profiles.Get(racer.Transponder) is null)
            .ToList();

        if (added.Count > 0)
        {
            profiles.UpsertMany(added);
        }

        for (var i = 0; i < added.Count; i++)
        {
            var transponder = added[i].Transponder;

            // Six digits, made up - in memory only, as above.
            settings.RacerPins[transponder] = (100_000 + i * 7_919 % 900_000).ToString();

            if (i % 2 == 0 && (added[i].Email.Length > 0 || added[i].Contact.Length > 0))
            {
                settings.RacerPinsEmailed.Add(transponder);
            }
        }

        var addedTransponders = added.Select(racer => racer.Transponder).ToList();

        void Tidy()
        {
            if (addedTransponders.Count > 0)
            {
                ProfileStore.Load().PutBack(Array.Empty<DriverProfile>(), addedTransponders);
            }
        }

        try
        {
            var form = new RacerPinsForm(settings, profiles);
            form.Disposed += (_, _) => Tidy();

            return form;
        }
        catch
        {
            Tidy();
            throw;
        }
    }

    /// <summary>An address made from a made-up name: "alexandria.montgomery-fitzwilliam.12@example.com" for the longest.</summary>
    private static string PointsPinsEmailFor(string name) =>
        name.ToLowerInvariant().Replace(' ', '.') + "@example.com";
}
