namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Online sign-up's windows (SignupForms.cs): Online Sign-ups itself -
    /// a club's worth of sign-ups and invites, and a fresh install's none -
    /// and the four it opens: Approve, for a driver with their own
    /// transponder and for one who wants a rental; Invite Drivers; and the
    /// one-line prompt as Turn Down and as Code for Someone Here.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> SignupWindows() => new (string, Func<Form>)[]
    {
        ("Online Sign-ups", () => OnlineSignupsWindow(filled: true)),
        ("Online Sign-ups (nothing yet)", () => OnlineSignupsWindow(filled: false)),
        ("Approve", () => ApproveSignupWindow(wantsRental: false)),
        ("Approve (wants a rental, signed up by a parent)", () => ApproveSignupWindow(wantsRental: true)),
        ("Invite Drivers", () => new InviteDriversForm()),
        ("Turn Down", () => new SignupReasonForm("Alexandria Montgomery-Fitzwilliam 12")),
        ("Code for Someone Here", () => new SignupReasonForm(null)),
    };

    /// <summary>
    /// Online Sign-ups on a desk that goes nowhere near the network. The
    /// desk reaches the relay only through its RelayPublisher, and this one
    /// has settings of its own with the relay off: its worker then waits and
    /// does nothing (RelayPublisher.Run), and an answer to a phone is only
    /// queued. It is stopped with the window.
    ///
    /// Filled: sign-up switched on, and the relay set up in the settings the
    /// desk and the window read - so the state line says sign-up is open -
    /// with twelve invites and six sign-ups waiting (FillSignupDesk). Not
    /// filled: a fresh install - the relay off, no invites, nothing waiting.
    /// </summary>
    private static SignupsForm OnlineSignupsWindow(bool filled)
    {
        var settings = new AppSettings();

        if (filled)
        {
            settings.RelayEnabled = true;
            settings.RelayBaseUrl = "https://relay.example.org";
            settings.RelayClub = "acme-rc";

            // The club's salt already made, so making invites doesn't make
            // one and save these settings (PinKdf.EnsureSalt) - with the relay
            // on - where a window built after this one would load them.
            settings.RelayPinSalt = new string('5', PinKdf.SaltBytes * 2);
        }

        var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });

        try
        {
            var profiles = ProfileStore.Load();
            var classes = RaceClassStore.Load();
            var rentals = RentalTransponderStore.Load();
            var desk = new RelaySignupDesk(settings, profiles, CheckInStore.Load(), RaceEventStore.Load(), classes, rentals, publisher, _ => { });

            if (filled)
            {
                FillSignupDesk(desk, settings);
            }

            var form = new SignupsForm(desk, settings, profiles, classes, rentals);
            form.Disposed += (_, _) => publisher.Dispose();

            return form;
        }
        catch
        {
            publisher.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A club's fortnight of online sign-up, in the desk's store in memory
    /// (nothing saved): twelve invites - emailed and not yet, given out at
    /// the desk, signed up, turned down, cancelled and expired, the longest
    /// for a long name's parent at a long address - and six sign-ups
    /// waiting, with names, emails, cars and notes of every length, the last
    /// two made with invites this laptop doesn't know.
    /// </summary>
    private static void FillSignupDesk(RelaySignupDesk desk, AppSettings settings)
    {
        var drivers = Drivers(12);

        desk.Store.Enabled = true;

        // Made the way the desk makes them: a code each, and the hash the
        // relay knows it by (PinKdf - a few hundredths of a second each).
        var invites = drivers
            .Select((driver, i) => desk.Store.Create(
                settings,
                i % 4 == 3 ? string.Empty : SignupEmailFor(driver),
                i % 3 == 0 ? $"{driver.DriverName}'s dad" : string.Empty))
            .ToList();

        for (var i = 0; i < invites.Count; i += 2)
        {
            if (invites[i].Email.Length > 0)
            {
                invites[i].EmailedUtc = DateTime.UtcNow;
            }
        }

        invites[6].State = SignupInviteState.Approved;
        invites[6].AnsweredUtc = DateTime.UtcNow;
        invites[6].Transponder = drivers[6].Transponder;

        invites[7].State = SignupInviteState.Rejected;
        invites[7].AnsweredUtc = DateTime.UtcNow;
        invites[7].AnswerMessage = "Please sign up at the desk on race day.";

        invites[8].State = SignupInviteState.Cancelled;
        invites[8].AnsweredUtc = DateTime.UtcNow;

        invites[9].ExpiresUtc = DateTime.UtcNow.AddDays(-2);

        var submitted = new DateTime(2026, 9, 26, 18, 5, 0, DateTimeKind.Utc);

        desk.Collected(Enumerable.Range(0, 6)
            .Select(i => new RelaySignupRequest(
                Hash: i < 4 ? invites[i].Hash : new string((char)('a' + i), 64),
                Name: drivers[i].DriverName,
                ClassName: i % 2 == 0 ? "Stock Buggy" : "Mini-Z MR-03 Modified",
                TransponderKind: (i % 3) switch
                {
                    0 => "own",
                    1 => "rental",
                    _ => "unknown",
                },
                Transponder: i % 3 == 0 ? drivers[i].Transponder : string.Empty,
                Car: drivers[i].CarModel,
                Email: i == 5 ? string.Empty : SignupEmailFor(drivers[i]),
                Guardian: i % 2 == 1,
                SubmittedUtc: submitted.AddMinutes(i * 13)))
            .ToList());
    }

    /// <summary>A made-up address from a driver's name - the longest names give the longest addresses.</summary>
    private static string SignupEmailFor(DriverProfile driver) =>
        $"{driver.DriverName.ToLowerInvariant().Replace(' ', '.')}@example.com";

    /// <summary>
    /// Approve for the longest name there is, with the club's classes to
    /// pick from. Wanting a rental: signed up by a parent, in a class the
    /// club doesn't have - three lines of intro, and a choice added to the
    /// list - with four of the club's rentals on the shelf to lend.
    /// </summary>
    private static ApproveSignupForm ApproveSignupWindow(bool wantsRental)
    {
        var classes = RaceClassStore.Load();

        // Added once in a data folder, however many windows are built in it.
        foreach (var name in new[] { "Stock Buggy", "Mini-Z MR-03 Modified", "Novice" })
        {
            if (!classes.All.Any(c => c.Name == name))
            {
                classes.Upsert(new RaceClass { Name = name });
            }
        }

        var rentals = RentalTransponderStore.Load();

        if (wantsRental)
        {
            for (var i = 1; i <= 4; i++)
            {
                rentals.Upsert(new RentalTransponder { Transponder = (5_000_000 + i * 41).ToString(), AssetTag = $"Club rental {i}" });
            }
        }

        var driver = Drivers(12)[11];

        var request = new RelaySignupRequest(
            Hash: new string('a', 64),
            Name: driver.DriverName,
            ClassName: wantsRental ? "Mini-Z AWD Touring Open" : "Stock Buggy",
            TransponderKind: wantsRental ? "rental" : "own",
            Transponder: wantsRental ? string.Empty : driver.Transponder,
            Car: driver.CarModel,
            Email: SignupEmailFor(driver),
            Guardian: wantsRental,
            SubmittedUtc: new DateTime(2026, 9, 26, 18, 5, 0, DateTimeKind.Utc));

        return new ApproveSignupForm(request, ProfileStore.Load(), classes, rentals);
    }
}
