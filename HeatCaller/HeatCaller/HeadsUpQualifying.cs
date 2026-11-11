namespace HeatCaller;

/// <summary>
/// Heads-up qualifying, the HRQF shape (11 Nov 2026, race-night build plan,
/// Phase 5.2). Chosen on Race Day as "First round seeded: Heads-up (HRQF)".
///
/// WHY: dealing a class in turn, or grouping it by pace, puts drivers of
/// very different speed on track together or keeps the quick ones apart
/// from each other all night. Heads-up does neither: the field is seeded
/// from tonight's PRACTICE (best three consecutive laps - a clean run, not
/// one lucky lap), and each heat takes every other seed - with two heats,
/// seeds 1/3/5/7 in one and 2/4/6/8 in the other - so every driver has the
/// driver just quicker and just slower than them on track at the same time.
/// Round 2 runs the same heats with the grid reversed, so the back of the
/// grid gets the clear track once. Round 3 is reseeded on the points from
/// rounds 1 and 2 and spread the same way. Scored with points qualifying
/// (QualifyingMethod.RoundPoints), and the mains are seeded on those points.
///
/// The program is built in one go, before round 2 has run, so round 3 is
/// first built from the practice seed. Pressing Build heats again once
/// rounds 1 and 2 are recorded rebuilds the unrun round 3 on points - the
/// build never touches a round that has run (RaceProgramControl.
/// BuildQualifying), so nothing else moves.
///
/// Pure: the program page hands it the roster, the practice runs and the
/// points order, so every heat it can produce is a table test.
/// </summary>
public static class HeadsUpQualifying
{
    /// <summary>The run length practice is seeded on - three in a row, as HRQF has it.</summary>
    public const int PracticeLaps = 3;

    /// <summary>
    /// Each driver's best run of <see cref="PracticeLaps"/> consecutive flying
    /// laps in practice sessions since <paramref name="sinceUtc"/>, keyed by
    /// the driver's main transponder.
    ///
    /// A practice session counts when it is a Practice session (open practice,
    /// no round) or a Practice round of this class. A practice round of
    /// another class does not: a driver's Mod pace is not their Stock pace.
    /// </summary>
    public static Dictionary<string, long> PracticeRuns(string className, IEnumerable<SessionRecord> history,
        Func<string, RoundDefinition?> roundOf, DateTime sinceUtc, Func<string, string?>? primaryForAlternate = null)
    {
        var best = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in history)
        {
            if (session.IsNotScored() || session.StartedAtUtc < sinceUtc)
            {
                continue;
            }

            var round = string.IsNullOrEmpty(session.RoundId) ? null : roundOf(session.RoundId);
            var isPractice = round is null
                ? session.SessionType == RaceSessionType.Practice
                : round.Kind == RoundKind.Practice
                  && string.Equals(round.ClassName?.Trim(), className.Trim(), StringComparison.OrdinalIgnoreCase);

            if (!isPractice)
            {
                continue;
            }

            foreach (var result in session.Results)
            {
                if (string.IsNullOrWhiteSpace(result.Transponder) || GhostService.IsGhost(result.Transponder))
                {
                    continue;
                }

                var (run, _) = QualifyingCalculator.BestFlyingWindow(result.LapTimesMs, PracticeLaps);

                if (run <= 0)
                {
                    continue;
                }

                var driver = primaryForAlternate?.Invoke(result.Transponder) ?? result.Transponder;

                if (!best.TryGetValue(driver, out var had) || run < had)
                {
                    best[driver] = run;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The seed order, quickest first: drivers with a practice run by it,
    /// then those without by <paramref name="fallbackPace"/> (the recent best
    /// lap the pace seeding uses), then the rest in roster order. A driver
    /// who missed practice is seeded rather than dropped.
    /// </summary>
    public static List<string> Seed(IReadOnlyList<string> roster, IReadOnlyDictionary<string, long> practice,
        IReadOnlyDictionary<string, long> fallbackPace)
    {
        return roster
            .Select((d, i) => (
                Driver: d,
                Index: i,
                Tier: practice.TryGetValue(d, out var run) && run > 0 ? 0 : fallbackPace.TryGetValue(d, out var lap) && lap > 0 ? 1 : 2,
                Ms: practice.TryGetValue(d, out var r) && r > 0 ? r : fallbackPace.TryGetValue(d, out var l) && l > 0 ? l : 0))
            .OrderBy(x => x.Tier)
            .ThenBy(x => x.Ms)
            .ThenBy(x => x.Index)
            .Select(x => x.Driver)
            .ToList();
    }

    /// <summary>
    /// Every other seed into each heat: seed 1 to heat 1, seed 2 to heat 2,
    /// and round again - so with two heats 1/3/5/7 and 2/4/6/8. Each heat's
    /// grid is in seed order, quickest on pole. Heats stay within one driver
    /// of each other.
    /// </summary>
    public static List<List<string>> Spread(IReadOnlyList<string> seeded, int heatCount)
    {
        var count = Math.Max(1, Math.Min(heatCount, Math.Max(1, seeded.Count)));
        var heats = Enumerable.Range(0, count).Select(_ => new List<string>()).ToList();

        for (var i = 0; i < seeded.Count; i++)
        {
            heats[i % count].Add(seeded[i]);
        }

        return heats;
    }

    /// <summary>The same heats with each grid turned round - the slowest seed on pole.</summary>
    public static List<List<string>> Reversed(IEnumerable<IReadOnlyList<string>> heats) =>
        heats.Select(h => h.Reverse().ToList()).ToList();

    /// <summary>
    /// One round's heats. <paramref name="round"/> is 0-based. Round 1 spreads
    /// the practice seed; round 2 reverses round 1's grids; round 3 spreads
    /// <paramref name="pointsOrder"/> (the points after rounds 1 and 2), or
    /// the practice seed again when that is not known yet. Further rounds keep
    /// the pattern: an even round reverses the one before it, an odd one
    /// reseeds on the latest points.
    /// </summary>
    /// <param name="seed">The practice seed order (see <see cref="Seed"/>).</param>
    /// <param name="heatCount">How many heats a round has.</param>
    /// <param name="pointsOrder">The points order to reseed on, or null when it can't be known yet.</param>
    public static List<List<string>> Round(int round, IReadOnlyList<string> seed, int heatCount, IReadOnlyList<string>? pointsOrder)
    {
        var reseeded = round >= 2 && pointsOrder is { Count: > 0 } ? WithEveryone(pointsOrder, seed) : seed;
        var spread = Spread(round >= 2 ? reseeded : seed, heatCount);

        return round % 2 == 1 ? Reversed(spread) : spread;
    }

    /// <summary>
    /// <paramref name="order"/> narrowed to the drivers in <paramref name="seed"/>
    /// (the class's roster tonight), with anybody it does not hold - a late
    /// entry with no points - added at the back in seed order.
    /// </summary>
    public static List<string> WithEveryone(IReadOnlyList<string> order, IReadOnlyList<string> seed)
    {
        var inRoster = new HashSet<string>(seed, StringComparer.OrdinalIgnoreCase);
        var ranked = order.Where(inRoster.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var placed = new HashSet<string>(ranked, StringComparer.OrdinalIgnoreCase);

        ranked.AddRange(seed.Where(d => !placed.Contains(d)));
        return ranked;
    }
}
