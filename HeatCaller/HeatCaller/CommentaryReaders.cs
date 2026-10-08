namespace HeatCaller;

/// <summary>
/// Turns the club's own stores into the flat records the fact builders
/// want.
///
/// WHY THE READERS ARE SEPARATE FROM THE BUILDERS
///
/// The builders decide what is worth saying and how to word it, and every
/// one of them is a table test because nothing it touches knows what a
/// store is. That stays true only if something else does the reading -
/// this. It is the one place in the commentary layer that knows about
/// the session history, the qualifying calculator and the live
/// championship, and
/// it holds no state and makes no judgements: stores in, records out.
///
/// It reads the session history directly for a driver's form rather than
/// going through DriverStats, which counts a driver across every class
/// they race - see GridHistory for why that matters.
///
/// WHAT IT DELIBERATELY DOES NOT WORK OUT
///
/// Several fields on those records are left unset here, and each one
/// silences a line rather than guessing at it:
///
///  - A driver's season qualifying average, so no "biggest mover".
///  - Which qualifiers made the top main, so no "missed it by".
///  - Who can still win a championship, which needs the rounds left and
///    the points on offer - see Championship.
///
/// Every one of those is a real reader of its own. An approximation would
/// be worse than silence: these lines are spoken over a PA as fact, and
/// the first one a driver can disprove is the last one anybody believes.
/// </summary>
public static class CommentaryReaders
{
    /// <summary>
    /// How recently a win stops being a drought worth mentioning. Inside
    /// this, "their first win since last month" is not a story.
    /// </summary>
    private static readonly TimeSpan RecentWin = TimeSpan.FromDays(45);

    /// <summary>How far back a run of form is read. More than this and it stops being form.</summary>
    private const int RecentRaces = 8;

    /// <summary>How many pairs to offer the intro. Ten drivers are forty-five pairs; three is a race intro.</summary>
    private const int MostRivalries = 3;

    /// <summary>
    /// The drivers in a race as the club's history knows them, counted
    /// over one class's races.
    ///
    /// WHY THIS IS NOT DriverStats
    ///
    /// DriverStats already works all of this out and is tested and
    /// trusted - but it counts a driver across everything they race. At a
    /// club running Box Stock and Open that makes "Sam has won the last
    /// two" a sentence about two different championships, and a driver
    /// who can disprove a line is a driver who stops believing the next
    /// one. So the races are filtered to the class first and counted
    /// here, over the same history DriverStats reads.
    ///
    /// Club-wide is still the answer when no class is given - a club
    /// night that files nothing against a class gets the same lines about
    /// the club as a whole, and the builders are told which it was so the
    /// wording follows (see CommentaryHistoryFacts).
    /// </summary>
    /// <param name="transponders">Who is in the race.</param>
    /// <param name="className">Whose races to count. Blank counts every race the club has run.</param>
    /// <param name="profiles">For the driver behind a transponder.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="rounds">Which round belongs to which class.</param>
    /// <param name="now">Today, for working out how long ago a win was.</param>
    public static (IReadOnlyList<DriverForm> Grid, IReadOnlyList<HeadToHead> Rivalries) GridHistory(
        IReadOnlyList<string> transponders,
        string? className,
        ProfileStore profiles,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        DateTime? now = null)
    {
        var grid = new List<DriverForm>();
        var rivalries = new List<HeadToHead>();

        var racing = transponders
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (racing.Count == 0)
        {
            return (grid, rivalries);
        }

        var races = RacesIn(className, history, rounds);

        if (races.Count == 0)
        {
            return (grid, rivalries);
        }

        var today = now ?? DateTime.UtcNow;

        foreach (var transponder in racing)
        {
            var name = (profiles.Get(transponder)?.DriverName ?? string.Empty).Trim();

            if (name.Length == 0)
            {
                continue;
            }

            var starts = 0;
            var wins = 0;
            var podiums = 0;
            var finishes = new List<int>();
            DateTime? lastWin = null;

            // Newest first, which is the order RecentFinishes is read in
            // and the order a streak is counted from.
            foreach (var race in races)
            {
                if (Placing(race, transponder) is not { } position)
                {
                    continue;
                }

                starts++;

                // A DNF is a zero, and it still counts as a start - "three
                // races without finishing" is a story, and it is not one
                // if the races are not counted.
                if (finishes.Count < RecentRaces)
                {
                    finishes.Add(position);
                }

                if (position == 1)
                {
                    wins++;
                    lastWin ??= race.EndedAtUtc;
                }

                if (position is >= 1 and <= 3)
                {
                    podiums++;
                }
            }

            // A driver with no starts in this class is a debut, and the
            // zeroes say so without any special case.
            grid.Add(new DriverForm(
                transponder,
                name,
                starts,
                wins,
                podiums,
                finishes,
                Since(lastWin, today)));
        }

        return (grid, HeadToHeads(grid, races));
    }

    /// <summary>
    /// Every race the club has run in a class, newest first.
    ///
    /// Practice is left out - a practice session has no result anybody
    /// would call a finish - and so is anything filed against a round of
    /// another class. With no class given, every race counts.
    /// </summary>
    private static List<SessionRecord> RacesIn(string? className, SessionHistoryStore history, RoundDefinitionStore rounds)
    {
        var all = history.All;
        var wanted = (className ?? string.Empty).Trim();

        IReadOnlySet<string>? roundIds = null;

        if (wanted.Length > 0)
        {
            roundIds = rounds.All
                .Where(r => !string.IsNullOrWhiteSpace(r.Id) && string.Equals(r.ClassName?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Id)
                .ToHashSet(StringComparer.Ordinal);

            if (roundIds.Count == 0)
            {
                return new List<SessionRecord>();
            }
        }

        return all
            .Where(record => record.SessionType != RaceSessionType.Practice)
            .Where(record => roundIds is null || (record.RoundId is { } id && roundIds.Contains(id)))
            .OrderByDescending(record => record.EndedAtUtc)
            .ToList();
    }

    /// <summary>Where a driver came in a race, or null when they were not in it.</summary>
    private static int? Placing(SessionRecord race, string transponder)
    {
        foreach (var entry in race.Results)
        {
            if (string.Equals(entry.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
            {
                // Anything that is not a placing is a retirement, which
                // DriverForm reads as a zero.
                return entry.Position > 0 ? entry.Position : 0;
            }
        }

        return null;
    }

    /// <summary>
    /// Which pairs on this grid have a record against each other worth
    /// knowing, best first.
    ///
    /// Capped, and deliberately: ten drivers make forty-five pairs, and a
    /// race intro that is nothing but head-to-heads is a database being
    /// read aloud. The builder has its own bar for what counts as a
    /// rivalry (CommentaryHistoryFacts.Rivalry); this only decides which
    /// few are worth offering it.
    /// </summary>
    private static List<HeadToHead> HeadToHeads(IReadOnlyList<DriverForm> grid, List<SessionRecord> races)
    {
        var found = new List<(HeadToHead Pair, int Met, double Lopsided)>();

        for (var i = 0; i < grid.Count; i++)
        {
            for (var j = i + 1; j < grid.Count; j++)
            {
                var a = grid[i].DriverId;
                var b = grid[j].DriverId;

                var aAhead = 0;
                var bAhead = 0;

                foreach (var race in races)
                {
                    // Only when both actually finished: a retirement says
                    // nothing about who is quicker, and counting it as a
                    // win for the other one is how a seven-one record gets
                    // built out of three races and four breakdowns.
                    var placedA = Placing(race, a) ?? 0;
                    var placedB = Placing(race, b) ?? 0;

                    if (placedA <= 0 || placedB <= 0)
                    {
                        continue;
                    }

                    if (placedA < placedB)
                    {
                        aAhead++;
                    }
                    else if (placedB < placedA)
                    {
                        bAhead++;
                    }
                }

                var met = aAhead + bAhead;

                if (met == 0)
                {
                    continue;
                }

                found.Add((
                    new HeadToHead(a, b, aAhead, bAhead),
                    met,
                    (double)Math.Max(aAhead, bAhead) / met));
            }
        }

        return found
            .OrderByDescending(p => p.Lopsided)
            .ThenByDescending(p => p.Met)
            .Take(MostRivalries)
            .Select(p => p.Pair)
            .ToList();
    }

    /// <summary>
    /// A class's qualifying, in order.
    ///
    /// The calculator's own result, read rather than recomputed - a second
    /// implementation that disagreed with the grid sheet by a place would
    /// be worse than no segment at all.
    ///
    /// Season averages and the main's cut are left unset: see the class
    /// summary. What comes back supports pole and the margin, which is the
    /// line the report leads with anyway.
    /// </summary>
    public static IReadOnlyList<QualifyingLine> Qualifying(
        string className,
        RoundDefinitionStore rounds,
        SessionHistoryStore history,
        ProfileStore profiles,
        RaceClass? raceClass = null,
        IReadOnlyCollection<string>? roundFilter = null)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return Array.Empty<QualifyingLine>();
        }

        List<QualifyingCalculator.Entry> order;

        try
        {
            order = QualifyingCalculator.Compute(className, rounds, history, profiles, raceClass, roundFilter);
        }
        catch
        {
            return Array.Empty<QualifyingLine>();
        }

        var lines = new List<QualifyingLine>(order.Count);

        for (var i = 0; i < order.Count; i++)
        {
            var entry = order[i];

            if (string.IsNullOrWhiteSpace(entry.DriverName))
            {
                continue;
            }

            lines.Add(new QualifyingLine(
                entry.Transponder,
                entry.DriverName,
                i + 1,
                entry.BestFastLapMs > 0 ? entry.BestFastLapMs / 1000.0 : 0));
        }

        return lines;
    }

    /// <summary>
    /// The championship as it stands, from the live table the Big Screen
    /// and the ticker already show.
    ///
    /// The before-and-after comes free: ChampionshipLive works out each
    /// driver's change as part of building the table, so there is no need
    /// to snapshot the standings ahead of the race - which was the one
    /// part of this segment that looked awkward and turned out not to be.
    ///
    /// EVERYBODY IS MARKED AS STILL IN IT
    ///
    /// Whether a title is mathematically settled needs the rounds left and
    /// the points on offer, and neither is here. So nothing is claimed:
    /// with every driver alive, the "that settles it" line never fires, and
    /// "N drivers can still win" only fires for a field of four or fewer -
    /// where, early in a season, it is usually true. It is still a claim
    /// this cannot stand behind, and a reader that knows the season's rules
    /// is what would let it.
    /// </summary>
    public static IReadOnlyList<ChampionshipStanding> Championship(IfItEndedNow? table)
    {
        if (table is null || table.Rows.Count == 0)
        {
            return Array.Empty<ChampionshipStanding>();
        }

        var leaderPoints = table.Rows[0].Points;
        var standings = new List<ChampionshipStanding>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.DriverName))
            {
                continue;
            }

            // Change is positions GAINED - see ChampionshipLive.Arrow, which
            // draws a up-arrow for a positive one. So where they were is
            // where they are plus what they gained.
            standings.Add(new ChampionshipStanding(
                row.DriverName,
                row.DriverName,
                row.Position,
                Math.Max(1, row.Position + row.Change),
                row.Points,
                Math.Max(0, leaderPoints - row.Points)));
        }

        return standings;
    }

    /// <summary>
    /// How long ago a win was, in the words a commentator would use - or
    /// null when it was recent enough that saying so would be odd.
    ///
    /// The caller's words rather than the fact builder's, because only
    /// something holding a calendar can tell "March" from "March last
    /// year", and a recap that says "their first win since last month" is
    /// describing a good run as a drought.
    /// </summary>
    private static string? Since(DateTime? lastWinUtc, DateTime now)
    {
        if (lastWinUtc is not { } won || won > now || now - won < RecentWin)
        {
            return null;
        }

        var month = won.ToString("MMMM");

        return won.Year == now.Year ? month : $"{month} {now.Year - won.Year} year{(now.Year - won.Year == 1 ? "" : "s")} ago";
    }
}
