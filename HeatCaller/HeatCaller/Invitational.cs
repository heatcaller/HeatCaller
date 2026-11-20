namespace HeatCaller;

/// <summary>
/// The Invitational (Reedy-style format) (19 Nov 2026, race-night build plan
/// row 14b).
///
/// WHAT IT IS: an invited field - the class's entries, or the first so many
/// of them - races a series of mains (5 unless the class says otherwise),
/// every driver in every main. Each finish scores points: by default 1st = 1,
/// 2nd = 2 and so on, a main not started scores one more than the field -
/// the scale the Reedy Race preset and the A-main legs use - or the class's
/// own round points scale. The best N mains count (4 of 5 unless the class
/// says otherwise) and fewest points wins. Level on points goes to the
/// better best single finish, then the better finish in the last main.
///
/// THE GRIDS: main 1 starts in seed order - the qualifying order when the
/// class has qualified, then the desk's own seed order for anybody without
/// a qualifying result, then the rest of the entry. Every later main is
/// gridded on the points so far, the leader at the front - or, with "invert
/// the grid", the leader at the back. Standings are shown after each main,
/// and the final result is the standings after the last.
///
/// The scoring is AMainLegs.Overall's - the same table, the same tie-break,
/// the same handling of a DQ, a ghost and a spare transponder - so the two
/// formats can't drift apart. The mains are ordinary tier-0 mains on the
/// programme with RoundDefinition.InvitationalMain saying which one.
///
/// Pure. Always called "Invitational (Reedy-style format)", or just
/// "Invitational" where there is no room - never by the event's own name.
/// </summary>
public static class Invitational
{
    /// <summary>The format's full name.</summary>
    public const string Name = "Invitational (Reedy-style format)";

    /// <summary>The short form, for a tile, a column or a call.</summary>
    public const string ShortName = "Invitational";

    public const int MinMains = 2;
    public const int MaxMains = 12;
    public const int DefaultMains = 5;
    public const int DefaultCounted = 4;

    /// <summary>What a main is called on the programme: "Invitational Main 2".</summary>
    public static string MainLabel(int main) => $"{ShortName} Main {main}";

    /// <summary>The class's number of mains, 2 to 12.</summary>
    public static int MainsFor(RaceClass? raceClass) => Math.Clamp(raceClass?.InvitationalMains ?? DefaultMains, MinMains, MaxMains);

    /// <summary>How many mains count for a series of <paramref name="mains"/> when the class chose <paramref name="counted"/>: 1 to all of them, and all but one when it chose nothing.</summary>
    public static int Counting(int mains, int counted)
    {
        var total = Math.Clamp(mains, 1, MaxMains);

        return counted > 0 ? Math.Min(counted, total) : Math.Max(1, total - 1);
    }

    /// <summary>The class's own counting rule.</summary>
    public static int CountingFor(RaceClass? raceClass) => Counting(MainsFor(raceClass), raceClass?.InvitationalCounted ?? DefaultCounted);

    /// <summary>What a finish scores for this class: 1st = 1 (Reedy), or the class's round points scale.</summary>
    public static RoundPointsScale ScaleFor(RaceClass? raceClass) =>
        raceClass is { InvitationalScoring: InvitationalScoring.ClassScale } ? raceClass.RoundPointsScale : RoundPointsScale.Club;

    /// <summary>The scoring in a few words: "1st = 1 point" or "TQ 0, 2nd 2 (IFMAR)".</summary>
    public static string ScoringWords(RaceClass? raceClass) =>
        raceClass is { InvitationalScoring: InvitationalScoring.ClassScale }
            ? $"the class's scale, {QualifyingCalculator.ScaleWords(raceClass.RoundPointsScale)}"
            : "1st = 1 point";

    /// <summary>
    /// The invited field in seed order: the qualifying order first (those
    /// still in the roster), then the desk's seed order for anybody left,
    /// then the rest of the roster as it stands - cut to
    /// <paramref name="limit"/> drivers when there is one (0 for everyone).
    /// </summary>
    public static List<string> Field(
        IReadOnlyList<string> qualifyingOrder,
        IReadOnlyList<string> roster,
        IReadOnlyList<string>? seed,
        int limit)
    {
        var inRoster = roster.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var field = new List<string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in qualifyingOrder.Concat(seed ?? Array.Empty<string>()).Concat(roster))
        {
            if (!string.IsNullOrWhiteSpace(driver) && inRoster.Contains(driver) && taken.Add(driver))
            {
                field.Add(driver);
            }
        }

        return limit > 0 ? field.Take(limit).ToList() : field;
    }

    /// <summary>
    /// The series' mains, in running order, every one with the whole field
    /// in seed order - main 1's grid. Later mains are put in points order
    /// when the main before them is filed (NextGrid). No bump-ups: the
    /// whole field is already in every main.
    /// </summary>
    public static List<RoundDefinition> BuildMains(string className, IReadOnlyList<string> field, int mains, string trackLayoutName)
    {
        var count = Math.Clamp(mains, MinMains, MaxMains);
        var built = new List<RoundDefinition>();

        for (var main = 1; main <= count; main++)
        {
            built.Add(new RoundDefinition
            {
                ClassName = className,
                RoundLabel = MainLabel(main),
                Kind = RoundKind.Main,
                MainTier = 0,
                InvitationalMain = main,
                InvitationalMainCount = count,
                TrackLayoutName = trackLayoutName,
                AssignedTransponders = field.ToList(),
            });
        }

        return built;
    }

    /// <summary>
    /// The mains of the series <paramref name="one"/> belongs to, in main
    /// order: the class's Invitational mains among <paramref name="rounds"/>
    /// (tonight's), one per main number, and where there are several - built
    /// again - the one built with <paramref name="one"/>.
    /// </summary>
    public static List<RoundDefinition> SeriesOf(RoundDefinition one, IEnumerable<RoundDefinition> rounds)
    {
        if (one.InvitationalMain <= 0)
        {
            return new List<RoundDefinition>();
        }

        return rounds
            .Where(r => r.Kind == RoundKind.Main
                        && r.InvitationalMain > 0
                        && (one.InvitationalMainCount <= 0 || r.InvitationalMain <= one.InvitationalMainCount)
                        && string.Equals(r.ClassName, one.ClassName, StringComparison.OrdinalIgnoreCase))
            .Append(one)
            .GroupBy(r => r.InvitationalMain)
            .Select(g => g.Any(r => string.Equals(r.Id, one.Id, StringComparison.Ordinal))
                ? g.First(r => string.Equals(r.Id, one.Id, StringComparison.Ordinal))
                : g.MinBy(r => (r.CreatedAtUtc - one.CreatedAtUtc).Duration())!)
            .OrderBy(r => r.InvitationalMain)
            .ToList();
    }

    /// <summary>The standings after the mains filed so far.</summary>
    public sealed record Table(List<AMainLegs.Standing> Rows, int MainsRun, int Mains, int Counting, int LastMain)
    {
        /// <summary>Every main of the series is in: this is the result.</summary>
        public bool Final => Mains > 0 && MainsRun >= Mains;

        /// <summary>The top of the table - the leader, or the winner once Final.</summary>
        public AMainLegs.Standing? Leader => Rows.Count > 0 ? Rows[0] : null;
    }

    /// <summary>
    /// The standings from the series' mains filed so far.
    /// <paramref name="justRun"/> is a main just finished and not yet linked
    /// to its round. The field is every driver in any of the series' grids,
    /// so a driver who has not started yet still has a line. Null when no
    /// main has been filed.
    /// </summary>
    public static Table? Standings(
        IReadOnlyList<RoundDefinition> series,
        Func<string, SessionRecord?> sessionById,
        RaceClass? raceClass,
        Func<string, string?>? primaryForAlternate = null,
        Func<string, string>? nameOf = null,
        (RoundDefinition Round, SessionRecord Result)? justRun = null)
    {
        var filed = new List<(int Main, SessionRecord Result)>();

        foreach (var main in series.OrderBy(r => r.InvitationalMain))
        {
            var result = justRun is { } just && string.Equals(just.Round.Id, main.Id, StringComparison.Ordinal)
                ? just.Result
                : main.LinkedSessionId is { } id ? sessionById(id) : null;

            if (result is not null)
            {
                filed.Add((main.InvitationalMain, result));
            }
        }

        if (filed.Count == 0)
        {
            return null;
        }

        var mains = Math.Max(filed.Count, series.Select(r => r.InvitationalMainCount).DefaultIfEmpty(0).Max());
        var counting = Counting(mains, raceClass?.InvitationalCounted ?? DefaultCounted);

        var entrants = series
            .SelectMany(r => r.AssignedTransponders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => (t, nameOf?.Invoke(t) ?? t))
            .ToList();

        var rows = AMainLegs.Overall(
            filed.Select(f => f.Result).ToList(),
            counting,
            primaryForAlternate,
            filed.Select(f => f.Main).ToList(),
            ScaleFor(raceClass),
            "Main",
            entrants);

        return new Table(rows, filed.Count, mains, counting, filed[^1].Main);
    }

    /// <summary>
    /// The grid for the next main from the standings: points order, the
    /// leader at the front - or with <paramref name="invert"/>, reversed, the
    /// leader at the back. Only drivers in <paramref name="grid"/> (the next
    /// main's own field, so a driver taken out by hand stays out), and any of
    /// those the table has no line for after them in their own order.
    /// </summary>
    public static List<string> NextGrid(Table table, IReadOnlyList<string> grid, bool invert, Func<string, string?>? primaryForAlternate = null)
    {
        string Key(string t) => primaryForAlternate?.Invoke(t) ?? t;

        var ordered = new List<string>();

        foreach (var row in table.Rows)
        {
            var inGrid = grid.FirstOrDefault(t => string.Equals(Key(t), row.Transponder, StringComparison.OrdinalIgnoreCase));

            if (inGrid is not null && !ordered.Contains(inGrid, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(inGrid);
            }
        }

        if (invert)
        {
            ordered.Reverse();
        }

        return ordered.Concat(grid.Where(t => !ordered.Contains(t, StringComparer.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>
    /// The next main still to run after <paramref name="justRun"/> that has
    /// not had its grid set yet, or null - the last main, or the next one
    /// already gridded (or raced).
    /// </summary>
    public static RoundDefinition? NextToGrid(RoundDefinition justRun, IReadOnlyList<RoundDefinition> series) =>
        series
            .Where(r => r.InvitationalMain > justRun.InvitationalMain && !r.IsRecorded)
            .OrderBy(r => r.InvitationalMain)
            .FirstOrDefault() is { InvitationalGridSet: false } next
            ? next
            : null;

    /// <summary>
    /// The call after a main: "After main 2 of 5, Sam leads the
    /// Invitational on 3 points." - or after the last, "Sam wins the
    /// Invitational, best 4 of 5 mains."
    /// </summary>
    public static string Call(Table table, Func<AMainLegs.Standing, string> spokenName)
    {
        if (table.Leader is not { } leader)
        {
            return string.Empty;
        }

        var name = spokenName(leader).Trim();

        if (table.Final)
        {
            var how = table.Counting < table.Mains
                ? $"best {AMainLegs.Words(table.Counting)} of {AMainLegs.Words(table.Mains)} mains"
                : $"across all {AMainLegs.Words(table.Mains)} mains";

            return $"{name} wins the {ShortName}, {how}.";
        }

        var points = leader.Points == 1 ? "1 point" : $"{leader.Points} points";

        return $"After main {table.MainsRun} of {table.Mains}, {name} leads the {ShortName} on {points}.";
    }

    /// <summary>The recap's fact line: where the series stands, and the top three.</summary>
    public static string Fact(Table table, Func<AMainLegs.Standing, string> nameOf)
    {
        var top = table.Rows.Take(3).Select((s, i) => $"{i + 1}. {nameOf(s)} {s.Words()}");

        return $"This was main {table.MainsRun} of {table.Mains} of the {ShortName}, where the whole field races every "
               + $"main and the best {AMainLegs.Words(table.Counting)} count, fewest points wins. "
               + (table.Final ? "Final result: " : "Standings: ")
               + string.Join(", ", top) + "."
               + (table.Final && table.Leader is { } winner ? $" {nameOf(winner)} wins the {ShortName}." : string.Empty);
    }

    /// <summary>The tie-breaker in words, for the results and the docs.</summary>
    public const string TieBreakWords =
        "Level on points goes to the better best single finish, then the better finish in the last main run.";
}
