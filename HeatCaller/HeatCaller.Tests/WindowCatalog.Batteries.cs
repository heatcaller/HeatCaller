namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The Batteries page's windows (BatteryPackControl.cs): Add Pack, and
    /// the same window editing a retired pack with every field long - a type
    /// typed in rather than picked, and notes running to more lines than the
    /// box shows; and the picker Set Pack... opens (BatteryPackControl
    /// .PackPicker), with more packs in rotation than it shows - some with
    /// long labels and notes, the session's own pack near the end - and with
    /// only one.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> BatteryWindows() => new (string, Func<Form>)[]
    {
        ("Add Pack", () => new BatteryPackEditForm(null)),
        ("Edit Pack (every field long)", () => new BatteryPackEditForm(BatteryLongPack())),
        ("Which pack? (many packs, long names)", () => BatteryPicker(count: 18, tagged: true)),
        ("Which pack? (one pack)", () => BatteryPicker(count: 1, tagged: false)),
    };

    /// <summary>
    /// A retired pack with every field as long as a club would make it: a
    /// label that says whose it is and how to tell it apart, a type typed in
    /// that is not one of the four on the list, and notes running to more
    /// lines than the box shows.
    /// </summary>
    private static BatteryPack BatteryLongPack() => new()
    {
        Label = "Team Orion Rocket LiHV 850mAh - red tape 12",
        Chemistry = "LiHV 4.35V per cell",
        Notes = "Bought at the swap meet, 14 June 2026.\r\n"
            + "Puffed slightly after the club final - practice only until it has been checked.\r\n"
            + "Keep it off the charger overnight, and charge it at 0.8A at most.\r\n"
            + "Retired until the replacement arrives from the club's supplier.",
        Retired = true,
    };

    /// <summary>
    /// Packs in rotation, in the order the store lists them (by label):
    /// every third with a long label and a note long enough to run past the
    /// list's edge, and some others with a note of two lines - the box they
    /// are typed in takes several - so the picker's lines come in every
    /// length.
    /// </summary>
    private static List<BatteryPack> BatteryPacks(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new BatteryPack
            {
                Label = i % 3 == 0 ? $"Team Orion Rocket LiHV 850mAh - red tape {i}" : $"Pack {i}",
                Chemistry = i % 5 == 0 ? "LiHV 4.35V" : i % 2 == 0 ? "LiPo" : "NiMH",
                Notes = i % 3 == 0
                    ? "Puffed slightly after the club final - practice only, and keep it off the charger overnight"
                    : i % 4 == 0
                        ? "Bought 2025\r\nNew connector fitted in March"
                        : string.Empty,
            })
            .OrderBy(pack => pack.Label)
            .ToList();

    /// <summary>
    /// The picker over <paramref name="count"/> packs, the session tagged with
    /// the last of them when <paramref name="tagged"/> - so the list opens
    /// with a pack out of sight selected - or with none, when the first is.
    /// </summary>
    private static BatteryPackControl.PackPicker BatteryPicker(int count, bool tagged)
    {
        var packs = BatteryPacks(count);

        return new BatteryPackControl.PackPicker(packs, tagged ? packs[packs.Count - 1] : null);
    }
}
