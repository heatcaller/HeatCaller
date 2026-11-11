namespace HeatCaller;

/// <summary>
/// Puts a round's heats in an order that gives every driver a race off
/// between their races (11 Nov 2026, race-night build plan, Phase 1, the
/// rest-time guard).
///
/// WHY: a round is built class by class - Stock's heats, then Mod's - so a
/// driver in both classes who lands in Stock's last heat and Mod's first
/// races back to back, with no time to swap cars, batteries or
/// transponders. LiveTime only colours those drivers; this avoids them.
///
/// THE RULE: keep the order the round was built in, and only where the next
/// heat would put a driver straight back on track, bring forward the
/// earliest later heat that doesn't. When every heat left clashes, the
/// earliest goes anyway - a club with one tiny class has no other choice,
/// and Auto start's rest for back-to-back drivers covers that case.
/// </summary>
public static class RestOrder
{
    /// <summary>
    /// The heats in rest-friendly order: indexes into <paramref name="heats"/>.
    /// </summary>
    /// <param name="heats">Each heat's drivers (transponders), in the order they were built.</param>
    /// <param name="before">The drivers of the race that runs just before the first of these, if any.</param>
    public static List<int> Arrange(IReadOnlyList<IReadOnlyCollection<string>> heats, IReadOnlyCollection<string>? before = null)
    {
        var left = Enumerable.Range(0, heats.Count).ToList();
        var order = new List<int>(heats.Count);
        IReadOnlyCollection<string>? last = before;

        while (left.Count > 0)
        {
            var pick = left.FirstOrDefault(i => !Shares(heats[i], last), -1);

            if (pick < 0)
            {
                pick = left[0];
            }

            order.Add(pick);
            left.Remove(pick);
            last = heats[pick];
        }

        return order;
    }

    /// <summary>How many times a driver races twice in a row in this order, for the console and the tests.</summary>
    public static int BackToBacks(IReadOnlyList<IReadOnlyCollection<string>> heatsInOrder, IReadOnlyCollection<string>? before = null)
    {
        var count = 0;
        var last = before;

        foreach (var heat in heatsInOrder)
        {
            if (Shares(heat, last))
            {
                count++;
            }

            last = heat;
        }

        return count;
    }

    private static bool Shares(IReadOnlyCollection<string> heat, IReadOnlyCollection<string>? other) =>
        other is { Count: > 0 }
        && heat.Any(t => !string.IsNullOrWhiteSpace(t) && other.Contains(t, StringComparer.OrdinalIgnoreCase));
}
