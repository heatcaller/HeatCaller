namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Hearing a name before it is read to a room (10 Oct): the roster
    /// checked one name at a time (NameCheckForm), from Driver Hub's Check
    /// Names... - full, with the longest respelling the line under the list
    /// can ever say; with nobody to check; and opened from a page with no
    /// voice to read through, which is a different introduction across the
    /// top. And a driver's profile with the Hear It beside its Announcer
    /// name box, which is only there when the page that opened it can read:
    /// the catalogue's other driver profiles are built without one, so they
    /// measure the dialog without it.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> NameCheckWindows()
    {
        // Twelve drivers, the longest name long enough to wrap in a column,
        // with a respelling on every third - the longest of those long
        // enough to be the longest "Saying:" line the window can show.
        static List<DriverProfile> Roster()
        {
            var drivers = Drivers(12);

            for (var i = 0; i < drivers.Count; i += 3)
            {
                drivers[i].AnnouncerName = i == 3
                    ? "Alex-ZAN-dria the Rocket MUNT-gum-ree FITZ-will-yum"
                    : $"Driver {i + 1} as it sounds";
            }

            // One driver nobody has named, whom the announcer reads out as a
            // transponder number - the sort of thing the window is for.
            drivers.Add(new DriverProfile { Transponder = "8812345" });

            return drivers;
        }

        // Nothing is ever read: AudioBus.Silent is on for the whole test run
        // and the sweep presses nothing. It is here so the windows are
        // measured with their buttons live, as a club sees them.
        static void Reads(string name)
        {
        }

        return new (string, Func<Form>)[]
        {
            ("Check How Names Are Read", () => new NameCheckForm(Roster(), Reads)),
            ("Check How Names Are Read (nobody to check)", () => new NameCheckForm(new List<DriverProfile>(), Reads)),
            ("Check How Names Are Read (no voice)", () => new NameCheckForm(Roster())),

            ("Driver Profile (hearing the name)", () => new ProfileForm(
                new DriverProfile
                {
                    Transponder = "4000137",
                    DriverName = "Alexandria Montgomery-Fitzwilliam",

                    // Longer than the box, so the hint under it and the
                    // button under that are measured against a field that
                    // has as much in it as one ever will.
                    AnnouncerName = "Alex-ZAN-dria the Rocket MUNT-gum-ree FITZ-will-yum",
                    CarModel = "Kyosho Mini-Z MR-03 EVO Sports",
                    ClassName = "Stock Buggy",
                },
                RaceClassStore.Load(),
                profiles: ProfileStore.Load(),
                speakName: Reads)),
        };
    }
}
