namespace HeatCaller;

/// <summary>
/// Double and triple A-mains (11 Nov 2026, race-night build plan, Phase 5.3).
///
/// WHY: a single A-main decides the night on one start - a first-corner
/// tangle and the fastest driver all evening finishes eighth. Big meetings
/// run the A-main as two or three LEGS and score the best of them, so one
/// bad start can be dropped. Each leg is scored like a qualifying round on
/// points: 1st = 1 point, 2nd = 2 and so on, a leg not started (or a DQ)
/// scores one more than the field, the class's best N legs are totalled and
/// fewest points wins.
///
/// THE TIE-BREAKER, stated so it can be printed: level on points goes to
/// the better best-single-leg finish, then to the better finish in the LAST
/// leg run (the one raced with everything at stake), then to the order the
/// legs' results were found in.
///
/// The legs themselves are ordinary tier-0 mains on the program, labelled
/// "A-Main Leg 1", "Leg 2"... (MainsBuilder.BuildRounds) with
/// RoundDefinition.MainLeg saying which one, so the announcer, the Big
/// Screen and the results all name the leg that is running from its label.
/// Each leg takes the bump-ups from the main below like any A-main.
///
/// Pure: it is handed each leg's recorded result, so it is a table test.
/// </summary>
public static class AMainLegs
{
    /// <summary>The most legs an A-main can be split into.</summary>
    public const int MaxLegs = 3;

    /// <summary>One driver's overall A-main result across the legs.</summary>
    public sealed record Standing(
        string Transponder,
        string DriverName,
        int Points,
        int LegsCounted,
        int LegsRun,
        int BestFinish,
        int LastLegFinish,
        string Detail)
    {
        /// <summary>The number of the first leg in the table - 1 unless leg 1 is not among the recorded ones.</summary>
        public int FirstLeg { get; init; } = 1;

        /// <summary>What one race of the series is called: "Leg" for an A-main's legs, "Main" for an Invitational's mains (19 Nov 2026).</summary>
        public string RaceWord { get; init; } = "Leg";

        /// <summary>"3 pts (best 2 of 3 legs)", "2 pts (2 legs)", or "1 pt (Leg 1)" after one leg.</summary>
        public string Words()
        {
            var total = Points == 1 ? "1 pt" : $"{Points} pts";
            var many = RaceWord.ToLowerInvariant() + "s";

            return LegsRun <= 1 ? $"{total} ({RaceWord} {FirstLeg})"
                : LegsCounted < LegsRun ? $"{total} (best {LegsCounted} of {LegsRun} {many})"
                : $"{total} ({LegsRun} {many})";
        }
    }

    /// <summary>
    /// The legs that count for a class of <paramref name="legs"/> legs when
    /// it chose <paramref name="counted"/> (0 for the usual rule): best 2 of
    /// 3 for a triple, best 1 of 2 for a double - the drop is the point.
    /// </summary>
    public static int LegsCounting(int legs, int counted)
    {
        var total = Math.Clamp(legs, 1, MaxLegs);

        return counted > 0 ? Math.Min(counted, total) : Math.Max(1, total - 1);
    }

    /// <summary>What a leg is called on the program: "A-Main Leg 2".</summary>
    public static string LegLabel(int leg) => LegLabel(0, leg);

    /// <summary>A leg of any main, for lower mains run in legs (19 Nov 2026): "B-Main Leg 2".</summary>
    public static string LegLabel(int tier, int leg) => $"{MainsBuilder.SeedPlan.LetterFor(Math.Max(0, tier))}-Main Leg {leg}";

    /// <summary>
    /// The overall result. <paramref name="legs"/> is each RECORDED leg's
    /// result in leg order (a leg not yet run is left out, so the table after
    /// leg 1 is leg 1's result). Ghosts are left out; a driver's alternate
    /// transponder is folded into their main one, and their better row in a
    /// leg stands. <paramref name="legNumbers"/> is each of those legs' own
    /// number, for when they are not simply 1, 2, 3: with leg 2 skipped or
    /// not yet in, leg 3's result used to be printed as "Leg 2".
    ///
    /// 19 Nov 2026: the Invitational (Reedy-style format) scores its mains
    /// the same way, so the same table serves it. <paramref name="scale"/>
    /// is what a finish scores - the club's 1st = 1, as the legs always
    /// were, or IFMAR's or EFRA's top finisher 0 (QualifyingCalculator.PlacePoints),
    /// with each scale's score for a race not started. <paramref name="raceWord"/>
    /// names one race of the series ("Leg", "Main"). <paramref name="entrants"/>
    /// is the whole field when it is known (an Invitational's invited
    /// drivers), so a driver who has not started a race yet still has a
    /// line, and the field + 1 is counted over all of them.
    /// </summary>
    public static List<Standing> Overall(
        IReadOnlyList<SessionRecord> legs,
        int countBest,
        Func<string, string?>? primaryForAlternate = null,
        IReadOnlyList<int>? legNumbers = null,
        RoundPointsScale scale = RoundPointsScale.Club,
        string raceWord = "Leg",
        IReadOnlyList<(string Transponder, string Name)>? entrants = null)
    {
        string Key(string t) => primaryForAlternate?.Invoke(t) ?? t;

        int Number(int i) => legNumbers is not null && i < legNumbers.Count ? legNumbers[i] : i + 1;

        // Each leg's finishing order, one place per driver.
        var orders = new List<List<(string Driver, string Name)>>();
        var dqs = new List<HashSet<string>>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var leg in legs)
        {
            var order = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dq = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var result in leg.Results
                         .Where(r => !string.IsNullOrWhiteSpace(r.Transponder) && !GhostService.IsGhost(r.Transponder))
                         .OrderBy(r => r.Outcome == ResultEditor.Dq ? 1 : 0)
                         .ThenBy(r => r.Position <= 0 ? int.MaxValue : r.Position)
                         .ThenByDescending(r => r.Laps)
                         .ThenBy(r => r.ElapsedMs))
            {
                var driver = Key(result.Transponder);
                names.TryAdd(driver, string.IsNullOrWhiteSpace(result.DriverName) ? driver : result.DriverName);

                if (result.Outcome == ResultEditor.Dq)
                {
                    dq.Add(driver);
                    continue;
                }

                // A row with no laps never crossed the line (a grid car put
                // on the board by a hand-released green, or added by hand and
                // never tapped): in the field, but it did not start this leg.
                if (result.Laps <= 0)
                {
                    continue;
                }

                if (seen.Add(driver))
                {
                    order.Add((driver, names[driver]));
                }
            }

            dq.ExceptWith(seen);
            orders.Add(order);
            dqs.Add(dq);
        }

        foreach (var (transponder, name) in entrants ?? Array.Empty<(string, string)>())
        {
            if (!string.IsNullOrWhiteSpace(transponder) && !GhostService.IsGhost(transponder))
            {
                var driver = Key(transponder);
                names.TryAdd(driver, string.IsNullOrWhiteSpace(name) ? driver : name);
            }
        }

        var field = names.Count;
        var missed = scale switch
        {
            RoundPointsScale.Ifmar => QualifyingCalculator.IfmarMissedRoundPoints,
            RoundPointsScale.Efra => field,
            _ => field + 1,
        };
        var count = Math.Max(1, countBest);
        var standings = new List<Standing>();

        foreach (var driver in names.Keys)
        {
            var finishes = orders
                .Select(o => o.FindIndex(x => string.Equals(x.Driver, driver, StringComparison.OrdinalIgnoreCase)) + 1)
                .ToList();
            var points = finishes.Select(f => f > 0 ? QualifyingCalculator.PlacePoints(f, scale) : missed).ToList();

            var counted = points
                .Select((p, i) => (p, i))
                .OrderBy(x => x.p)
                .ThenBy(x => x.i)
                .Take(Math.Min(count, points.Count))
                .ToList();

            var detail = string.Join(", ", finishes.Select((f, i) =>
                $"{raceWord} {Number(i)} {(f > 0 ? Ordinal(f) : dqs[i].Contains(driver) ? "DQ" : "DNS")} ({(points[i] == 1 ? "1 pt" : $"{points[i]} pts")})"
                + (counted.Any(c => c.i == i) ? string.Empty : " dropped")));

            standings.Add(new Standing(
                driver,
                names[driver],
                counted.Sum(c => c.p),
                counted.Count,
                legs.Count,
                finishes.Where(f => f > 0).DefaultIfEmpty(0).Min(),
                finishes.Count > 0 ? finishes[^1] : 0,
                detail)
            {
                FirstLeg = Number(0),
                RaceWord = raceWord,
            });
        }

        static int OrLast(int finish) => finish > 0 ? finish : int.MaxValue;

        return standings
            .OrderBy(s => s.Points)
            .ThenBy(s => OrLast(s.BestFinish))
            .ThenBy(s => OrLast(s.LastLegFinish))
            .ToList();
    }

    /// <summary>The overall A-main once its last leg is in: the table, how many legs counted and how many ran.</summary>
    public sealed record Decided(List<Standing> Table, int Counting, int Legs)
    {
        /// <summary>The overall winner - the top of the table.</summary>
        public Standing Winner => Table[0];

        /// <summary>Which main this is: 0 for the A-main, 1 for the B-main and so on (19 Nov 2026, lower mains in legs).</summary>
        public int Tier { get; init; }

        /// <summary>"A", "B"... - the main's letter.</summary>
        public string Letter => MainsBuilder.SeedPlan.LetterFor(Tier);
    }

    /// <summary>A main's tier, an untiered main read as the top one - the reading the legs have always used.</summary>
    private static int TierOf(RoundDefinition round) => round.MainTier <= 0 ? 0 : round.MainTier;

    /// <summary>
    /// Every leg of the main <paramref name="leg"/> is a leg of, among
    /// <paramref name="rounds"/>, in leg order: one round per leg number of
    /// the same class and tier, and where there are several the one built
    /// nearest <paramref name="leg"/> (a main's legs are made together) -
    /// <paramref name="leg"/> itself for its own number. Just
    /// <paramref name="leg"/> when it is not a leg.
    /// </summary>
    public static List<RoundDefinition> LegsOf(RoundDefinition leg, IEnumerable<RoundDefinition> rounds)
    {
        if (leg.MainLeg <= 0)
        {
            return new List<RoundDefinition> { leg };
        }

        return rounds
            .Where(r => r.Kind == RoundKind.Main
                        && r.MainLeg > 0
                        && TierOf(r) == TierOf(leg)
                        && r.MainLeg != leg.MainLeg
                        && (leg.MainLegCount <= 0 || r.MainLeg <= leg.MainLegCount)
                        && !string.Equals(r.Id, leg.Id, StringComparison.Ordinal)
                        && string.Equals(r.ClassName, leg.ClassName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.MainLeg)
            .Select(g => g.MinBy(r => (r.CreatedAtUtc - leg.CreatedAtUtc).Duration())!)
            .Append(leg)
            .OrderBy(r => r.MainLeg)
            .ToList();
    }

    /// <summary>
    /// The overall A-main result when <paramref name="justRun"/> is the LAST
    /// leg of its A-main to be recorded (11 Nov 2026) - else null: not a leg,
    /// another leg still to run, or nobody scored. <paramref name="justRunResult"/>
    /// is the leg just finished, handed in because the caller asks before the
    /// round is linked to it. The other legs are the class's other legs in
    /// <paramref name="roundsInScope"/> (tonight's), each read through its
    /// linked session.
    /// </summary>
    public static Decided? AfterLastLeg(
        RoundDefinition justRun,
        SessionRecord justRunResult,
        IEnumerable<RoundDefinition> roundsInScope,
        Func<string, SessionRecord?> sessionById,
        int countedSetting,
        Func<string, string?>? primaryForAlternate = null)
    {
        // Any tier since 19 Nov 2026 (lower mains in legs): the legs of the
        // main this one is a leg of.
        if (justRun is not { Kind: RoundKind.Main, MainLeg: > 0 })
        {
            return null;
        }

        // WHY (Bug hunt 17): one round for each OTHER leg number of this
        // A-main, and where there are several, the one built with this leg
        // (MainsBuilder makes an A-main's legs together). With no race day
        // open the scope is every round there is, and the class's legs from
        // an earlier meeting were taken as legs of tonight's: six legs scored
        // into tonight's winner, or no winner at all if an old one never ran.
        var siblings = LegsOf(justRun, roundsInScope).Where(r => !ReferenceEquals(r, justRun)).ToList();

        var legs = new List<(int Leg, SessionRecord Result)> { (justRun.MainLeg, justRunResult) };

        foreach (var sibling in siblings)
        {
            // A leg still to run means this one is not the last.
            if (!sibling.IsRecorded || sibling.LinkedSessionId is not { } id || sessionById(id) is not { } result)
            {
                return null;
            }

            legs.Add((sibling.MainLeg, result));
        }

        var legCount = Math.Max(justRun.MainLegCount, legs.Count);

        if (legs.Count < legCount || legs.Count < 2)
        {
            return null;
        }

        var inOrder = legs.OrderBy(l => l.Leg).ToList();
        var ordered = inOrder.Select(l => l.Result).ToList();
        var counting = LegsCounting(legCount, countedSetting);
        var table = Overall(ordered, counting, primaryForAlternate, inOrder.Select(l => l.Leg).ToList());

        return table.Count == 0 ? null : new Decided(table, counting, legCount) { Tier = TierOf(justRun) };
    }

    /// <summary>
    /// The announcer's line for the overall winner: "Sam wins the A-Main
    /// overall, best two of three legs." <paramref name="winner"/> is the
    /// name as it is said.
    /// </summary>
    public static string OverallCall(string winner, int counting, int legs, string letter = "A")
    {
        var how = counting < legs
            ? $"best {Words(counting)} of {Words(legs)} legs"
            : $"across all {Words(legs)} legs";

        // The main's own letter (19 Nov 2026): "wins the B-Main overall".
        return $"{winner.Trim()} wins the {letter}-Main overall, {how}.";
    }

    /// <summary>The recap's fact line for the overall result: winner, points and the next two.</summary>
    public static string OverallFact(Decided decided, Func<Standing, string> nameOf)
    {
        var top = decided.Table.Take(3).Select((s, i) => $"{i + 1}. {nameOf(s)} {s.Words()}");

        return $"This was the last leg of the {decided.Letter}-Main, which is scored over {Words(decided.Legs)} legs with the best "
               + $"{Words(decided.Counting)} counting. Overall: {string.Join(", ", top)}. "
               + $"{nameOf(decided.Winner)} wins the {decided.Letter}-Main overall.";
    }

    internal static string Words(int n) => n switch
    {
        1 => "one",
        2 => "two",
        3 => "three",
        _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>The tie-breaker in words, for the results and the docs.</summary>
    public const string TieBreakWords =
        "Level on points goes to the better best single-leg finish, then the better finish in the last leg run.";

    internal static string Ordinal(int n) => (n % 100) is 11 or 12 or 13
        ? $"{n}th"
        : (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };
}
