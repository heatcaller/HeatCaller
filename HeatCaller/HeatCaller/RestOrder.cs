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
///
/// 12 Nov 2026, setup plan step 8: the gap is the race day's "Races off
/// between a driver's races:" (RaceEvent.RacesOffBetween), 0 to 3, rather
/// than always one. With a gap of N, a heat clashes when any of its drivers
/// raced in the last N heats. When no heat left avoids that, the one whose
/// clash is furthest back goes - two races off beats one. And moving
/// drivers by hand, or dragging the running order, is checked against it
/// (<see cref="TooClose"/>) and warns before it saves.
/// </summary>
public static class RestOrder
{
    /// <summary>The one race off every build gave before the gap could be set.</summary>
    public const int DefaultGap = 1;

    /// <summary>The most races off Setup offers.</summary>
    public const int MostGap = 3;

    /// <summary>
    /// The heats in rest-friendly order with a one-race gap: indexes into
    /// <paramref name="heats"/>.
    /// </summary>
    /// <param name="heats">Each heat's drivers (transponders), in the order they were built.</param>
    /// <param name="before">The drivers of the race that runs just before the first of these, if any.</param>
    public static List<int> Arrange(IReadOnlyList<IReadOnlyCollection<string>> heats, IReadOnlyCollection<string>? before = null) =>
        Arrange(heats, before is null ? null : new[] { before }, DefaultGap);

    /// <summary>
    /// The heats in rest-friendly order for a gap of <paramref name="gap"/>
    /// races (setup plan step 8). <paramref name="recent"/> is the races that
    /// run just before the first of these, oldest first (only the last
    /// <paramref name="gap"/> matter). A gap of 0 keeps the built order.
    /// </summary>
    public static List<int> Arrange(IReadOnlyList<IReadOnlyCollection<string>> heats, IReadOnlyList<IReadOnlyCollection<string>>? recent, int gap)
    {
        gap = Math.Clamp(gap, 0, MostGap);
        var left = Enumerable.Range(0, heats.Count).ToList();
        var order = new List<int>(heats.Count);
        var last = (recent ?? Array.Empty<IReadOnlyCollection<string>>()).ToList();

        while (left.Count > 0)
        {
            var window = last.Skip(Math.Max(0, last.Count - gap)).ToList();

            // The earliest heat with nobody who raced in the window; failing
            // that, the one whose latest clash is furthest back.
            var pick = left.FirstOrDefault(i => ClashDistance(heats[i], window) is null, -1);

            if (pick < 0)
            {
                pick = left
                    .Select((i, place) => (i, place, Distance: ClashDistance(heats[i], window) ?? int.MaxValue))
                    .OrderByDescending(x => x.Distance)
                    .ThenBy(x => x.place)
                    .First().i;
            }

            order.Add(pick);
            left.Remove(pick);
            last.Add(heats[pick]);
        }

        return order;
    }

    /// <summary>How many times a driver races twice in a row in this order, for the console and the tests.</summary>
    public static int BackToBacks(IReadOnlyList<IReadOnlyCollection<string>> heatsInOrder, IReadOnlyCollection<string>? before = null) =>
        Clashes(heatsInOrder, before is null ? null : new[] { before }, DefaultGap);

    /// <summary>How many times a driver races with fewer than <paramref name="gap"/> races between, in this order.</summary>
    public static int Clashes(IReadOnlyList<IReadOnlyCollection<string>> heatsInOrder, IReadOnlyList<IReadOnlyCollection<string>>? recent, int gap)
    {
        gap = Math.Clamp(gap, 0, MostGap);
        var count = 0;
        var last = (recent ?? Array.Empty<IReadOnlyCollection<string>>()).ToList();

        foreach (var heat in heatsInOrder)
        {
            if (ClashDistance(heat, last.Skip(Math.Max(0, last.Count - gap)).ToList()) is not null)
            {
                count++;
            }

            last.Add(heat);
        }

        return count;
    }

    /// <summary>One driver too close to their last race: who, the two races, and how many races are between.</summary>
    public sealed record TooCloseRace(string Transponder, string First, string Second, int RacesBetween);

    /// <summary>
    /// Every driver who races closer than <paramref name="gap"/> races apart
    /// in this running order (setup plan step 8) - for the warning before a
    /// change by hand is saved. <paramref name="races"/> is (label, drivers)
    /// in running order; the race already run count too, so a change that
    /// puts a driver straight back out after the race just run is caught.
    /// One line per driver, their first clash.
    /// </summary>
    public static List<TooCloseRace> TooClose(IReadOnlyList<(string Label, IReadOnlyCollection<string> Drivers)> races, int gap) =>
        TooCloseAll(races, gap)
            .GroupBy(t => t.Transponder, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    /// <summary>
    /// The drivers a change by hand puts too close (setup plan step 8): those
    /// too close in <paramref name="after"/> who weren't already in
    /// <paramref name="before"/> - a clash the night already had, which
    /// the director has seen and lived with, isn't the change's fault.
    /// </summary>
    public static List<TooCloseRace> NewlyTooClose(IReadOnlyList<(string Label, IReadOnlyCollection<string> Drivers)> before,
        IReadOnlyList<(string Label, IReadOnlyCollection<string> Drivers)> after, int gap)
    {
        var already = TooCloseAll(before, gap).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return TooCloseAll(after, gap)
            .Where(t => !already.Contains(Key(t)))
            .GroupBy(t => t.Transponder, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        static string Key(TooCloseRace t) => $"{t.Transponder}|{t.First}|{t.Second}";
    }

    /// <summary>Every too-close pair, not only each driver's first - for comparing two orders.</summary>
    private static List<TooCloseRace> TooCloseAll(IReadOnlyList<(string Label, IReadOnlyCollection<string> Drivers)> races, int gap)
    {
        gap = Math.Clamp(gap, 0, MostGap);
        var found = new List<TooCloseRace>();
        var lastSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < races.Count && gap > 0; i++)
        {
            foreach (var driver in races[i].Drivers.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (lastSeen.TryGetValue(driver, out var before) && i - before - 1 < gap)
                {
                    found.Add(new TooCloseRace(driver, races[before].Label, races[i].Label, i - before - 1));
                }

                lastSeen[driver] = i;
            }
        }

        return found;
    }

    /// <summary>
    /// The warning, one line a driver: "Max Power would race in Q2 Heat 1
    /// and Q2 Heat 2 back to back." or "...with only 1 race between."
    /// </summary>
    public static string Warning(IReadOnlyList<TooCloseRace> tooClose, Func<string, string> nameOf, int gap)
    {
        var lines = tooClose.Select(t =>
            $"{nameOf(t.Transponder)} would race in {t.First} and {t.Second} "
            + (t.RacesBetween == 0 ? "back to back." : $"with only {Plural.Of(t.RacesBetween, "race")} between."));

        return string.Join("\r\n", lines)
               + $"\r\n\r\nTonight gives every driver {Plural.Of(gap, "race")} off between their races (Race Day > Setup).";
    }

    /// <summary>How many races back (1 = the last one) the nearest driver of <paramref name="heat"/> raced in <paramref name="window"/>, or null for none.</summary>
    private static int? ClashDistance(IReadOnlyCollection<string> heat, IReadOnlyList<IReadOnlyCollection<string>> window)
    {
        for (var back = 1; back <= window.Count; back++)
        {
            if (Shares(heat, window[window.Count - back]))
            {
                return back;
            }
        }

        return null;
    }

    private static bool Shares(IReadOnlyCollection<string> heat, IReadOnlyCollection<string>? other) =>
        other is { Count: > 0 }
        && heat.Any(t => !string.IsNullOrWhiteSpace(t) && other.Contains(t, StringComparer.OrdinalIgnoreCase));
}
