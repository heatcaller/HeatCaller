namespace HeatCaller;

/// <summary>
/// Re-sorting a qualifying round's heats by pace (Move drivers between
/// heats - Group by pace / Spread by pace).
///
/// Pace is each driver's best lap in the class over the last few weeks of
/// saved races - their recent form, not a lap from last season on a
/// different layout. A driver with no lap on record has no pace to sort by
/// and goes after everyone who has, in the order they were already in.
///
/// Two ways to use it, because clubs want both:
///
/// Group: similar pace together, fastest in the LAST heat - the usual way
/// to seed qualifying. Close racing in every heat, and the quickest cars
/// on track last, when the crowd is biggest.
///
/// Spread: every heat as evenly matched as the next, by snake draft - the
/// fastest to Heat 1, the second to Heat 2, ... back again from the last
/// heat. For a night of points-per-heat racing where a heat full of the
/// quick drivers would be unfair.
///
/// Either way every heat keeps the number of drivers it had, and each
/// heat's grid is fastest first.
/// </summary>
public static class HeatSeeding
{
    /// <summary>How far back a lap counts as recent form.</summary>
    public static readonly TimeSpan RecentForm = TimeSpan.FromDays(60);

    /// <summary>
    /// Each driver's best lap in <paramref name="className"/> since
    /// <paramref name="sinceUtc"/>, by transponder - from the saved races
    /// run for a round of that class. Demo races and ghosts left out.
    /// </summary>
    /// <param name="primaryForAlternate">
    /// ProfileStore.PrimaryForAlternate, so a lap set on a driver's
    /// ALTERNATE transponder is filed under their main one - the id the
    /// rosters being sorted hold. Left null, every lap stays under the id it
    /// was set on, as before.
    /// </param>
    public static Dictionary<string, long> BestLaps(
        string className, IEnumerable<SessionRecord> history, Func<string, RoundDefinition?> roundOf, DateTime sinceUtc,
        Func<string, string?>? primaryForAlternate = null)
    {
        var best = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in history)
        {
            if (session.IsNotScored() || session.StartedAtUtc < sinceUtc || string.IsNullOrEmpty(session.RoundId))
            {
                continue;
            }

            // A heat the class shared (11 Nov 2026, Phase 4.1, mixed-class
            // heats) counts too, for its own drivers only - a Mod lap is no
            // guide to a Stock heat.
            if (roundOf(session.RoundId) is not { } round
                || !MixedHeats.RunsClass(round, className))
            {
                continue;
            }

            foreach (var result in session.Results)
            {
                if (result.FastLapMs <= 0 || string.IsNullOrWhiteSpace(result.Transponder) || GhostService.IsGhost(result.Transponder))
                {
                    continue;
                }

                if (!MixedHeats.Counts(round, className, result.Transponder, result.ClassName))
                {
                    continue;
                }

                // Under the driver's main transponder. Keyed by the id the
                // lap was set on, a driver whose recent laps were all in
                // their second car had no pace at all on the roster - which
                // holds main transponders - and was sorted in with the
                // drivers nobody has a lap for.
                var driver = primaryForAlternate?.Invoke(result.Transponder) ?? result.Transponder;

                if (!best.TryGetValue(driver, out var had) || result.FastLapMs < had)
                {
                    best[driver] = result.FastLapMs;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// <paramref name="drivers"/> fastest first: those with a pace by it,
    /// then those without in the order given.
    /// </summary>
    public static List<string> ByPace(IEnumerable<string> drivers, IReadOnlyDictionary<string, long> pace)
    {
        var list = drivers.ToList();

        return list
            .Select((d, i) => (Driver: d, Index: i, Ms: pace.TryGetValue(d, out var ms) && ms > 0 ? ms : long.MaxValue))
            .OrderBy(x => x.Ms)
            .ThenBy(x => x.Index)
            .Select(x => x.Driver)
            .ToList();
    }

    /// <summary>
    /// Similar pace together: the fastest fill the last heat, the next
    /// fastest the one before it, and so on. <paramref name="sizes"/> is how
    /// many each heat holds, in running order.
    /// </summary>
    public static List<List<string>> Group(IReadOnlyList<int> sizes, IEnumerable<string> drivers, IReadOnlyDictionary<string, long> pace)
    {
        var ordered = new Queue<string>(ByPace(drivers, pace));
        var heats = sizes.Select(_ => new List<string>()).ToList();

        if (heats.Count == 0)
        {
            return heats;
        }

        for (var h = heats.Count - 1; h >= 0; h--)
        {
            while (heats[h].Count < sizes[h] && ordered.Count > 0)
            {
                heats[h].Add(ordered.Dequeue());
            }
        }

        // More drivers than places (never from the form, which only moves
        // the drivers already in the heats): the rest into the first heat.
        heats[0].AddRange(ordered);

        return heats;
    }

    /// <summary>
    /// Evenly matched: a snake draft - fastest to the first heat, next to
    /// the second, back from the last - passing over a heat that is full.
    /// </summary>
    public static List<List<string>> Spread(IReadOnlyList<int> sizes, IEnumerable<string> drivers, IReadOnlyDictionary<string, long> pace)
    {
        var heats = sizes.Select(_ => new List<string>()).ToList();

        if (heats.Count == 0)
        {
            return heats;
        }

        // One visit to every heat a pass, there and back: as many passes as
        // the biggest heat has places is enough for every heat to fill.
        var order = new List<int>();

        for (var lap = 0; lap < sizes.Max(); lap++)
        {
            var pass = Enumerable.Range(0, heats.Count);
            order.AddRange(lap % 2 == 0 ? pass : pass.Reverse());
        }

        var next = 0;

        foreach (var driver in ByPace(drivers, pace))
        {
            while (next < order.Count && heats[order[next]].Count >= sizes[order[next]])
            {
                next++;
            }

            if (next >= order.Count)
            {
                heats[0].Add(driver);
                continue;
            }

            heats[order[next]].Add(driver);
            next++;
        }

        return heats;
    }
}
