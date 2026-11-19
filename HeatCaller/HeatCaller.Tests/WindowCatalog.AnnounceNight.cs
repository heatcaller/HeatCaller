namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Announce the next night (AnnounceNightForm), as Next Steps opens it
    /// (18 Nov 2026, dialog jazz-up): a night not yet announced, with the
    /// club's classes to pick from, and one already up - with Take Down -
    /// whose classes include a long name and both times set. It is not on
    /// LayoutAudit.ConvertedWindows, so its findings are in the report
    /// without failing the sweep.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> AnnounceNightWindows() => new (string, Func<Form>)[]
    {
        ("Announce the Next Night", () => new AnnounceNightForm(
            new AnnouncedNight { Day = DateTime.Today.AddDays(6), Title = "Club Night" },
            new[] { "Stock Buggy", "Mod Buggy", "Stock Truck", "Short Course", "Mini-Z" },
            announcedAlready: false)),
        ("Announce the Next Night (up already)", () => new AnnounceNightForm(
            new AnnouncedNight
            {
                Day = DateTime.Today.AddDays(2),
                Title = "Winter Series Round 3",
                Venue = "Riverside Raceway",
                Classes = new List<string> { "Stock Buggy", "17.5 Blinky 2WD Modified Buggy (Juniors)" },
                FirstRace = TimeSpan.FromHours(19),
                EntriesClose = TimeSpan.FromHours(18.5),
            },
            new[] { "Stock Buggy", "Mod Buggy", "Stock Truck", "Short Course", "Mini-Z", "4WD Open", "Vintage" },
            announcedAlready: true)),
    };
}
