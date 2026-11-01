namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Paste a result (PasteResultForm), from Race History's Paste a
    /// Result...: a LiveRC A-main of twenty drivers - the longest names
    /// Drivers makes, half of them matched to a profile - with a long class
    /// name and a row left out; and a pasted table no driver could be read
    /// from, with Import switched off.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> PasteResultWindows() => new (string, Func<Form>)[]
    {
        ("Paste a result", () => PasteResultLiveRc(drivers: 20)),
        ("Paste a result (no drivers read)", () => PasteResultLiveRc(drivers: 0)),
    };

    /// <summary>
    /// The window as Race History opens it (RaceHistoryControl.PasteResult):
    /// <paramref name="drivers"/> rows in finishing order, every other one
    /// matched to a profile, and the race's title split into its class and
    /// round.
    /// </summary>
    private static PasteResultForm PasteResultLiveRc(int drivers)
    {
        var profiles = Drivers(Math.Max(drivers, 1));

        var rows = profiles
            .Take(drivers)
            .Select((driver, i) => new PastedResult.Row(
                i + 1,
                i % 2 == 0 ? driver.DriverName : driver.DriverName + " Junior",
                24 - i / 3,
                300_000 + i * 1_337,
                13_900 + i * 41,
                string.Empty))
            .ToList();

        var problems = drivers > 0
            ? new List<string> { $"Row {drivers + 1} has no driver, so it was left out." }
            : new List<string> { "Row 2 has no driver, so it was left out.", "Row 3 has no driver, so it was left out." };

        return new PasteResultForm(
            new PastedResult.Race(rows, problems),
            "LiveRC",
            "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors) A-Main",
            new[] { "Stock Buggy", "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)" },
            name => PastedResult.MatchByName(profiles, name),
            new DateTime(2026, 10, 3, 14, 30, 0));
    }
}
