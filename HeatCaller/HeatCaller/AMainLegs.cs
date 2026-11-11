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
        /// <summary>"3 pts (best 2 of 3 legs)", "2 pts (2 legs)", or "1 pt (Leg 1)" after one leg.</summary>
        public string Words()
        {
            var total = Points == 1 ? "1 pt" : $"{Points} pts";

            return LegsRun <= 1 ? $"{total} (Leg 1)"
                : LegsCounted < LegsRun ? $"{total} (best {LegsCounted} of {LegsRun} legs)"
                : $"{total} ({LegsRun} legs)";
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
    public static string LegLabel(int leg) => $"A-Main Leg {leg}";

    /// <summary>
    /// The overall result. <paramref name="legs"/> is each RECORDED leg's
    /// result in leg order (a leg not yet run is left out, so the table after
    /// leg 1 is leg 1's result). Ghosts are left out; a driver's alternate
    /// transponder is folded into their main one, and their better row in a
    /// leg stands.
    /// </summary>
    public static List<Standing> Overall(IReadOnlyList<SessionRecord> legs, int countBest, Func<string, string?>? primaryForAlternate = null)
    {
        string Key(string t) => primaryForAlternate?.Invoke(t) ?? t;

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

                if (seen.Add(driver))
                {
                    order.Add((driver, names[driver]));
                }
            }

            dq.ExceptWith(seen);
            orders.Add(order);
            dqs.Add(dq);
        }

        var field = names.Count;
        var missed = field + 1;
        var count = Math.Max(1, countBest);
        var standings = new List<Standing>();

        foreach (var driver in names.Keys)
        {
            var finishes = orders
                .Select(o => o.FindIndex(x => string.Equals(x.Driver, driver, StringComparison.OrdinalIgnoreCase)) + 1)
                .ToList();
            var points = finishes.Select(f => f > 0 ? f : missed).ToList();

            var counted = points
                .Select((p, i) => (p, i))
                .OrderBy(x => x.p)
                .ThenBy(x => x.i)
                .Take(Math.Min(count, points.Count))
                .ToList();

            var detail = string.Join(", ", finishes.Select((f, i) =>
                $"Leg {i + 1} {(f > 0 ? Ordinal(f) : dqs[i].Contains(driver) ? "DQ" : "DNS")} ({(points[i] == 1 ? "1 pt" : $"{points[i]} pts")})"
                + (counted.Any(c => c.i == i) ? string.Empty : " dropped")));

            standings.Add(new Standing(
                driver,
                names[driver],
                counted.Sum(c => c.p),
                counted.Count,
                legs.Count,
                finishes.Where(f => f > 0).DefaultIfEmpty(0).Min(),
                finishes.Count > 0 ? finishes[^1] : 0,
                detail));
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
        if (justRun is not { Kind: RoundKind.Main, MainTier: <= 0, MainLeg: > 0 })
        {
            return null;
        }

        var siblings = roundsInScope
            .Where(r => r.Kind == RoundKind.Main
                        && r.MainTier <= 0
                        && r.MainLeg > 0
                        && !string.Equals(r.Id, justRun.Id, StringComparison.Ordinal)
                        && string.Equals(r.ClassName, justRun.ClassName, StringComparison.OrdinalIgnoreCase))
            .ToList();

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

        var ordered = legs.OrderBy(l => l.Leg).Select(l => l.Result).ToList();
        var counting = LegsCounting(legCount, countedSetting);
        var table = Overall(ordered, counting, primaryForAlternate);

        return table.Count == 0 ? null : new Decided(table, counting, legCount);
    }

    /// <summary>
    /// The announcer's line for the overall winner: "Sam wins the A-Main
    /// overall, best two of three legs." <paramref name="winner"/> is the
    /// name as it is said.
    /// </summary>
    public static string OverallCall(string winner, int counting, int legs)
    {
        var how = counting < legs
            ? $"best {Words(counting)} of {Words(legs)} legs"
            : $"across all {Words(legs)} legs";

        return $"{winner.Trim()} wins the A-Main overall, {how}.";
    }

    /// <summary>The recap's fact line for the overall result: winner, points and the next two.</summary>
    public static string OverallFact(Decided decided, Func<Standing, string> nameOf)
    {
        var top = decided.Table.Take(3).Select((s, i) => $"{i + 1}. {nameOf(s)} {s.Words()}");

        return $"This was the last leg of the A-Main, which is scored over {Words(decided.Legs)} legs with the best "
               + $"{Words(decided.Counting)} counting. Overall: {string.Join(", ", top)}. "
               + $"{nameOf(decided.Winner)} wins the A-Main overall.";
    }

    private static string Words(int n) => n switch
    {
        1 => "one",
        2 => "two",
        3 => "three",
        _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>The tie-breaker in words, for the results and the docs.</summary>
    public const string TieBreakWords =
        "Level on points goes to the better best single-leg finish, then the better finish in the last leg run.";

    private static string Ordinal(int n) => (n % 100) is 11 or 12 or 13
        ? $"{n}th"
        : (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };
}
