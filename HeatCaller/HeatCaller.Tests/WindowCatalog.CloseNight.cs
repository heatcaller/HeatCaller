namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The closing checklist (11 Nov 2026): a night with everything still to
    /// do, long names and several of the club's own lines, and the same night
    /// all wrapped up.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> CloseNightWindows() => new (string, Func<Form>)[]
    {
        ("Close the Night", () => CloseNight(allDone: false)),
        ("Close the Night (all wrapped up)", () => CloseNight(allDone: true)),
    };

    private static CloseNightForm CloseNight(bool allDone)
    {
        var own = new List<string> { "Lock the clubhouse", "Pack up the timing loop and the transponder charger", "Switch off the track lights" };

        var facts = allDone
            ? new NightClose.Facts(Array.Empty<string>(), 0, 0, DateTime.UtcNow, false, Array.Empty<string>())
            : new NightClose.Facts(
                new[] { "Samantha Longname-Driverson", "Jo", "Al", "Bea" },
                14,
                3,
                null,
                true,
                new[] { "Christopher Worthington-Smythe", "Bea", "Cy", "Dee", "Eve" });

        var ticks = allDone ? own.Select(l => "own:" + l).ToHashSet() : new HashSet<string>();
        var items = CloseNightChecklist.Build(facts, DateTime.UtcNow.AddHours(-1), own, ticks);

        return new CloseNightForm(() => items, (_, _) => { }, _ => { }, () => own, _ => { });
    }
}
