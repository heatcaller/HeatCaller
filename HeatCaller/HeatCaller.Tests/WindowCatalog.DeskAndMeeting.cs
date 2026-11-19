namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The start-of-night windows and the desk's till slip (18 Nov 2026, the
    /// third dialog batch): Ready to Race (CloseNightForm's other list) with
    /// most of the night still to sort and the club's own lines; the drivers'
    /// meeting (DriversMeetingForm) built from a full night; and Driver Paid
    /// (DriverPaidForm) with a family on the slip, one of them settled
    /// already and one with the catalog's longest name, and with nobody on it.
    /// And the Desk View's PIN (DeskPinForm): chosen to lock the laptop to
    /// Who's Racing, and typed to unlock it.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> DeskAndMeetingWindows() => new (string, Func<Form>)[]
    {
        ("Ready to Race", ReadyToRaceWindow),
        ("Drivers' Meeting", () => new DriversMeetingForm(DriversMeeting.Text(new DriversMeeting.Facts(
            new RaceEvent
            {
                Name = "Club Night 12 Nov",
                QualifyingRounds = 3,
                FirstRaceLocalTime = TimeSpan.FromHours(19),
                OutByLocalTime = TimeSpan.FromHours(22),
                Breaks = { new PlannedBreak { AfterRound = 2, Minutes = 10, What = "water the track" } },
                Notes = "The back straight jump has been moved.",
            },
            new[] { "Juniors", "Stock", "Mod" },
            9,
            WaitsForMarshals: true,
            HasMarshalPosts: true,
            ProtestMinutes: 5,
            PapersChecked: true)))),
        ("Driver Paid", () => DriverPaidWindow(4)),
        ("Driver Paid (nobody yet)", () => DriverPaidWindow(0)),
        ("Desk View", () => new DeskPinForm(choosing: true)),
        ("Unlock the Desk View", () => new DeskPinForm(choosing: false)),
    };

    private static Form ReadyToRaceWindow()
    {
        var own = new List<string> { "Water the track", "Set out the marshal posts and the spare transponders" };
        var now = DateTime.UtcNow;

        var facts = new ReadyFacts(
            DecoderConnected: true,
            DecoderHeardTonight: false,
            PhoneServerRunning: false,
            WebsiteOnline: true,
            VoiceProblem: "no voice is set up",
            HeatsBuilt: true,
            HeatsPosted: false,
            NotHeard: new[] { "Christopher Worthington-Smythe", "Bea", "Cy" },
            PapersNotInOrder: new[] { "Samantha Longname-Driverson" },
            LastBackupUtc: now.AddDays(-30));

        var items = ReadyChecklist.Build(facts, now, own, new HashSet<string>());

        return new CloseNightForm(() => items, (_, _) => { }, _ => { }, () => own, _ => { }, ready: true);
    }

    private static Form DriverPaidWindow(int count)
    {
        var drivers = Drivers(Math.Max(count, 1)).Take(count).ToList();

        var form = new DriverPaidForm(
            _ => 15m,
            p => drivers.IndexOf(p) == 1 ? "Comp (Marshal)" : "Owes $15.00",
            (_, _, _) => { });

        form.AddDrivers(drivers);
        return form;
    }
}
