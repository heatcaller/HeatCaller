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
/// the session history, the programme, the qualifying calculator and the
/// live championship, and
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
    /// How many of tonight's remaining rounds are offered.
    ///
    /// Three, because that is what CommentaryFacts.ComingUpNext reads out
    /// and one reader should not decide for it. The continuity bridges take
    /// only the first (CommentaryContinuity.Ahead); the segment takes all
    /// three.
    /// </summary>
    private const int MostUpcoming = 3;

    /// <summary>
    /// How long after a race was filed "that was the B-main" is still true.
    ///
    /// It is a look-back, not a history lesson. A club breaks for tea, comes
    /// back an hour later and the first thing the announcer says must not be
    /// about the race before the sandwiches - and on a night that was
    /// abandoned after one heat, the next day's announcements must not open
    /// with "that was Q1 Heat 1" because that is still the newest thing
    /// filed. Twenty minutes is longer than any gap a programme plans and
    /// shorter than any break a club takes.
    /// </summary>
    private static readonly TimeSpan JustRunWithin = TimeSpan.FromMinutes(20);

    /// <summary>
    /// What is left on tonight's programme, in running order, with the
    /// estimate handed straight through - what CommentaryDirector.Upcoming
    /// wants.
    ///
    /// WHY THE ESTIMATE IS A PARAMETER AND NOT WORKED OUT HERE
    ///
    /// How long a club night actually runs against its schedule is
    /// WhereTimeGoes's question, and a reader that guessed at it would be
    /// inventing the one number an announcer gets held to - the rule
    /// CommentaryFacts.ComingUpNext states and this follows. Null is a
    /// perfectly good answer: every line that would have used it is said
    /// without it.
    ///
    /// WHAT <paramref name="skipRoundId"/> IS FOR, AND WHY IT MATTERS
    ///
    /// A round is only "recorded" once its race has been FILED, so in the
    /// window between the flag and the filing the race that has just
    /// finished is still the first unrecorded round on the programme - and
    /// "still to come, Q2 Heat 1" said about the heat the room has just
    /// watched is the worst thing a continuity line can do. The host knows
    /// which round is in hand and whether its race is over; it passes that
    /// round's id here and this leaves it out.
    /// </summary>
    /// <param name="rounds">Tonight's programme.</param>
    /// <param name="scope">The round ids belonging to the race day in hand, or null for the legacy holder where every round is tonight's - RaceEventStore.ActiveRoundScope's own answer.</param>
    /// <param name="inAbout">Roughly how long until the next one, where the host knows. Handed through untouched.</param>
    /// <param name="skipRoundId">The round in hand, where its race has already run and not yet been filed. See the remarks.</param>
    /// <param name="most">How many to offer. Defaults to <see cref="MostUpcoming"/>.</param>
    public static (IReadOnlyList<UpcomingRound> Rounds, TimeSpan? InAbout) Upcoming(
        RoundDefinitionStore rounds,
        IReadOnlySet<string>? scope = null,
        TimeSpan? inAbout = null,
        string? skipRoundId = null,
        int most = MostUpcoming)
    {
        var next = new List<UpcomingRound>();
        var want = Math.Max(1, most);

        // Unrecorded, in the programme's own order - the store's answer, not
        // a second opinion about what "still to run" means.
        foreach (var round in rounds.Unrecorded(scope))
        {
            if (!string.IsNullOrEmpty(skipRoundId)
                && string.Equals(round.Id, skipRoundId, StringComparison.Ordinal))
            {
                continue;
            }

            var label = Typed(round.RoundLabel);

            // A round with no label has no name to say. Left out rather
            // than given one: "still to come, round four" would be this
            // reader inventing what the club calls it.
            if (label.Length == 0)
            {
                continue;
            }

            // A roster of nobody is not an empty round - a main built from
            // the heats has no roster until the heats are in - so it is
            // offered with a count of none and the lines that would have
            // mentioned cars say nothing about them.
            next.Add(new UpcomingRound(
                label,
                Typed(round.ClassName),
                round.AssignedTransponders.Count));

            if (next.Count >= want)
            {
                break;
            }
        }

        return (next, inAbout);
    }

    /// <summary>
    /// The round whose race has just been run, for the look-back half of a
    /// continuity line - or null when there is nothing recent enough to
    /// look back on.
    ///
    /// Read from what has been FILED rather than from the programme's order,
    /// because a club runs races out of order all evening: the newest
    /// session with a result and a round behind it is the one the room has
    /// just watched, whatever position that round sits in. The same history
    /// the form guide is counted over, read the same way.
    ///
    /// Three things each silence it rather than guessing: a session filed
    /// against no round, a round belonging to another race day, and one
    /// filed longer ago than <see cref="JustRunWithin"/>. Nothing here is
    /// said unless the history says it.
    /// </summary>
    /// <param name="rounds">Tonight's programme, for the round's label and class.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="scope">The round ids belonging to the race day in hand, or null to accept any - see <see cref="Upcoming"/>.</param>
    /// <param name="now">Now, for how long ago it was. UTC, as the records are.</param>
    /// <param name="within">How recent it has to be. Defaults to <see cref="JustRunWithin"/>.</param>
    public static RoundJustRun? JustRan(
        RoundDefinitionStore rounds,
        SessionHistoryStore history,
        IReadOnlySet<string>? scope = null,
        DateTime? now = null,
        TimeSpan? within = null)
    {
        var newest = history.All
            .Where(record => record.SessionType != RaceSessionType.Practice)
            .Where(record => record.Results.Count > 0)
            .Where(record => !string.IsNullOrWhiteSpace(record.RoundId))
            .OrderByDescending(record => record.EndedAtUtc)
            .FirstOrDefault();

        if (newest?.RoundId is not { } roundId)
        {
            return null;
        }

        if (scope is not null && !scope.Contains(roundId))
        {
            return null;
        }

        var today = now ?? DateTime.UtcNow;

        // A record stamped in the future is a clock that has been put back,
        // which is not a race that has just run. The same care Since takes
        // with the same comparison.
        if (newest.EndedAtUtc > today || today - newest.EndedAtUtc > (within ?? JustRunWithin))
        {
            return null;
        }

        if (rounds.Get(roundId) is not { } round)
        {
            return null;
        }

        var label = Typed(round.RoundLabel);

        return label.Length == 0
            ? null
            : new RoundJustRun(label, Typed(round.ClassName));
    }

    /// <summary>
    /// A label or a class name as somebody typed it, trimmed, and empty
    /// where there is nothing in it.
    ///
    /// Null-tolerant although neither property is declared nullable: these
    /// come back from JSON on disk, which is perfectly capable of holding a
    /// null in a field the app says cannot be one, and a continuity line is
    /// not worth a NullReferenceException on a race night. The same care
    /// RacesIn above takes with the same two properties.
    /// </summary>
    private static string Typed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

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
