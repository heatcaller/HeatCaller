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
    /// How many past race days "this day at the club" is offered. Two -
    /// the figure CommentaryHistoryFacts.ThisDayFacts states and the
    /// reason it gives.
    /// </summary>
    private const int MostPastDays = CommentaryHistoryFacts.ThisDayFacts;

    /// <summary>
    /// How many championship lines a race intro is offered.
    ///
    /// Four, which is what Championship.MaxMoves gives the recap, for the
    /// reason stated there: more near-identical rows of name-and-number is
    /// how a small model starts reading one driver's points total against
    /// another driver's name.
    /// </summary>
    private const int MostStakes = 4;

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
    /// <param name="exceptSessionId">One saved race to leave out of the counting. See the remarks.</param>
    /// <remarks>
    /// WHY A RACE CAN BE LEFT OUT, AND WHY THE RECAP NEEDS IT
    ///
    /// The intro asks this before the race has run, so every saved race is
    /// history and nothing has to be excluded. The RECAP asks it after the
    /// result has been FILED - SpeakRaceSummary runs from RaceFiling's
    /// afterSaved callback - so by then tonight's race is in the history
    /// too. CommentaryHistoryFacts.ResultInContext states the caller's
    /// promise plainly: the record must be the one from BEFORE this race,
    /// "or every win is their first". A recap that counted tonight in
    /// would announce a driver's fourth win here as their fifth, and a
    /// first win as no first at all. So the recap passes the session it is
    /// recapping and this leaves it out of the counting.
    /// </remarks>
    public static (IReadOnlyList<DriverForm> Grid, IReadOnlyList<HeadToHead> Rivalries) GridHistory(
        IReadOnlyList<string> transponders,
        string? className,
        ProfileStore profiles,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        DateTime? now = null,
        string? exceptSessionId = null)
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

        var races = RacesIn(className, history, rounds, exceptSessionId);

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
    ///
    /// SO IS A DEMO RACE (10 Oct 2026). Its lap times and its finishing
    /// order are invented - SessionRecord.IsDemo says so and every other
    /// reader of the history honours it (Championship, ChampionshipLive,
    /// DriverStats, the relay's own index) - so a rehearsal with "Save demo
    /// races to history" ticked was being counted as a start, a win and a
    /// podium, and a driver's record read out over the PA included races
    /// nobody drove. The one thing these lines cannot be is wrong.
    ///
    /// <paramref name="exceptSessionId"/> takes one more race out: the one
    /// being recapped, which is already in the history by the time the
    /// recap is written. See GridHistory's remarks.
    /// </summary>
    private static List<SessionRecord> RacesIn(
        string? className,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        string? exceptSessionId = null)
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
            .Where(record => !record.IsDemoSession())
            .Where(record => string.IsNullOrEmpty(exceptSessionId)
                             || !string.Equals(record.Id, exceptSessionId, StringComparison.Ordinal))
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
    /// The championship a race about to be run counts towards, as the
    /// table stands BEFORE it - what the race has at stake, which is the
    /// one thing that makes two races with identical entries sound
    /// different.
    ///
    /// Null when the race counts towards no championship, which is most
    /// practice nights and every club that never set a season up, and null
    /// when nobody on this grid is in the table.
    ///
    /// WHY THIS IS NOT A SECOND SCORING IMPLEMENTATION
    ///
    /// Every number here comes out of SeasonStore.ComputeStandings over
    /// the races ChampionshipLive.Candidates says the season counts - the
    /// same two calls the Big Screen's "if it ended now" and the recap's
    /// own swing are built from, so the drop rule and the season's own
    /// rule about which races count are applied by the one place that
    /// applies them. Nothing in here adds, subtracts or predicts.
    ///
    /// WHY ChampionshipLive.Compute IS NOT USED DIRECTLY
    ///
    /// It needs a result to score, and refuses a race where nobody has a
    /// lap yet - which is every race at staging, when an intro is written.
    /// The candidates and the standings behind it are exactly what is
    /// wanted, so those are what this asks for.
    ///
    /// Call this on the UI thread, like everything else here: it reads the
    /// history, the programme and the event stores.
    /// </summary>
    /// <param name="round">The round about to run. A season's rule takes a race by its round's class and kind, so a freeform race counts towards nothing.</param>
    /// <param name="transponders">Who is on this grid.</param>
    /// <param name="seasons">The club's seasons, or null where there are none.</param>
    /// <param name="rounds">The programme, for the season's rule.</param>
    /// <param name="events">The race days, for the season's rule.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="profiles">For the driver behind a transponder.</param>
    /// <param name="most">How many of this grid's drivers to offer. Four - see <see cref="MostStakes"/>.</param>
    public static ChampionshipAtStake? ChampionshipBefore(
        RoundDefinition? round,
        IReadOnlyList<string> transponders,
        SeasonStore? seasons,
        RoundDefinitionStore? rounds,
        RaceEventStore? events,
        SessionHistoryStore history,
        ProfileStore? profiles = null,
        int most = MostStakes)
    {
        // The names the standings are keyed on. ComputeStandings keys on
        // the driver's name, so that is what has to be matched - the same
        // rule Championship.SwingFor follows, and the same limitation: a
        // driver renamed since their results were saved silently stops
        // matching, exactly as they do for a rivalry.
        var onThisGrid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in transponders)
        {
            if (string.IsNullOrWhiteSpace(transponder))
            {
                continue;
            }

            var name = Typed(profiles?.Get(transponder)?.DriverName);

            if (name.Length > 0)
            {
                onThisGrid.Add(name);
            }
        }

        if (onThisGrid.Count == 0)
        {
            return null;
        }

        foreach (var (season, counted) in ChampionshipLive.Candidates(round, seasons, rounds, events, history))
        {
            var before = SeasonStore.ComputeStandings(season, counted, profiles);

            if (before.Count == 0)
            {
                continue;
            }

            var leaderPoints = before[0].TotalPoints;
            var rows = new List<ChampionshipStanding>();

            for (var i = 0; i < before.Count && rows.Count < Math.Max(1, most); i++)
            {
                if (!onThisGrid.Contains(before[i].DriverName))
                {
                    continue;
                }

                // PositionNow and PositionBefore are the same figure on
                // purpose: the race has not been run, so nobody has moved.
                // StillAlive is left at its default and nothing here reads
                // it - whether a title is mathematically settled needs the
                // rounds left and the points on offer, and neither is here
                // (the same silence Championship above keeps).
                rows.Add(new ChampionshipStanding(
                    before[i].DriverName,
                    before[i].DriverName,
                    i + 1,
                    i + 1,
                    before[i].TotalPoints,
                    Math.Max(0, leaderPoints - before[i].TotalPoints)));
            }

            // A table nobody in this race is in is not what this race is
            // about. The next season might be.
            if (rows.Count == 0)
            {
                continue;
            }

            return new ChampionshipAtStake(
                Typed(season.Name),
                counted.Count,
                Typed(before[0].DriverName),
                onThisGrid.Contains(before[0].DriverName),
                rows);
        }

        return null;
    }

    /// <summary>
    /// Where a class stands partway through its heats - what the halfway
    /// standings segment is, and what it had no reader for until 10 Oct
    /// 2026.
    ///
    /// THE HEATS ONLY. RoundKind.Qualifier and nothing else: a class whose
    /// heats are all run and whose mains are still to come is not
    /// "partway through the heats", and counting the mains in would have
    /// the segment fire over the top of the qualifying report - which is a
    /// better line about the same order, with its own budget. Two segments
    /// reading out one order in one gap is the complaint, not the fix.
    ///
    /// THE ORDER IS THE CALCULATOR'S, through <see cref="Qualifying"/>
    /// above, so the club's own qualifying method decides it. A second
    /// implementation that disagreed with the grid sheet by a place would
    /// be worse than no segment at all.
    ///
    /// Null three ways, each of them the honest answer: no class on track,
    /// fewer heats run than the segment needs, and every heat already run.
    /// The bar is judged here as well as in the builder on purpose - the
    /// builder keeps the promise for a table test, and this saves walking
    /// the whole history through the calculator on a tick that was never
    /// going to say anything.
    ///
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="className">The class on track. Blank is no answer rather than every class.</param>
    /// <param name="rounds">Tonight's programme, for the class's heats and whether each has been recorded.</param>
    /// <param name="history">Every session the club has filed, for the calculator.</param>
    /// <param name="profiles">For the driver behind a transponder.</param>
    /// <param name="scope">The round ids belonging to the race day in hand, or null for the legacy holder - see <see cref="Upcoming"/>.</param>
    /// <param name="raceClass">The class's own qualifying method, where the club set one.</param>
    /// <param name="roundFilter">Tonight's rounds and no others, exactly as the programme tab does it - see MainForm's wiring for the August-pole bug this closes.</param>
    public static MeetingSoFar? MidMeeting(
        string? className,
        RoundDefinitionStore rounds,
        SessionHistoryStore history,
        ProfileStore profiles,
        IReadOnlySet<string>? scope = null,
        RaceClass? raceClass = null,
        IReadOnlyCollection<string>? roundFilter = null)
    {
        var wanted = Typed(className);

        if (wanted.Length == 0)
        {
            return null;
        }

        var heats = rounds.All
            .Where(round => !string.IsNullOrWhiteSpace(round.Id))
            .Where(round => round.Kind == RoundKind.Qualifier)
            .Where(round => scope is null || scope.Contains(round.Id))
            .Where(round => string.Equals(Typed(round.ClassName), wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (heats.Count == 0)
        {
            return null;
        }

        var run = heats.Count(round => round.IsRecorded);

        if (run < CommentaryReportFacts.MidMeetingLeast || run >= heats.Count)
        {
            return null;
        }

        var order = Qualifying(wanted, rounds, history, profiles, raceClass, roundFilter);

        return order.Count == 0 ? null : new MeetingSoFar(wanted, run, heats.Count, order);
    }

    /// <summary>
    /// What this club did on this date in other years - the third segment
    /// that had a tick box and no reader.
    ///
    /// WHY THE DATE IS THE LOCAL ONE
    ///
    /// Records are stamped in UTC and every other reader here compares
    /// them in UTC, which is right for "how long ago". It is wrong for
    /// "this date": a club night that starts at eight in the evening in
    /// New York is stamped the NEXT day in UTC, so a meeting on the 10th
    /// would be looked up as the 11th and a club would be told about a
    /// race day that was not the anniversary of anything. Both sides are
    /// converted, so both shift together or neither does.
    ///
    /// WHY THE WINNER'S NAME IS THE SAVED ONE
    ///
    /// The result holds the name as it was on the night. A transponder
    /// changes hands between seasons - TransponderTransfer exists for
    /// exactly that - so looking the profile up now can put tonight's
    /// owner's name on a race somebody else won two years ago. The saved
    /// name is who actually won it.
    ///
    /// Practice and demo races are left out for the reasons RacesIn gives:
    /// a practice session has no finish anybody would call a result, and a
    /// demo race's finishing order is invented.
    ///
    /// Empty for a club in its first year, which is most clubs that have
    /// just installed this - and empty is the right answer.
    /// </summary>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="now">Today, in local time. Defaults to now.</param>
    /// <param name="most">How many past days to offer. Two - see <see cref="MostPastDays"/>.</param>
    public static IReadOnlyList<ClubRaceDay> OnThisDay(
        SessionHistoryStore history,
        DateTime? now = null,
        int most = MostPastDays)
    {
        var today = (now ?? DateTime.Now).Date;
        var byDay = new Dictionary<DateTime, List<SessionRecord>>();

        foreach (var record in history.All)
        {
            if (record.SessionType == RaceSessionType.Practice
                || record.IsDemoSession()
                || record.Results.Count == 0)
            {
                continue;
            }

            var when = record.EndedAtUtc.ToLocalTime().Date;

            // Other years, so this year is out - which takes tonight's own
            // races with it, and a night that reminisced about itself an
            // hour ago is the one thing this segment must not do.
            if (when.Year >= today.Year || when.Month != today.Month || when.Day != today.Day)
            {
                continue;
            }

            if (!byDay.TryGetValue(when, out var sessions))
            {
                sessions = new List<SessionRecord>();
                byDay[when] = sessions;
            }

            sessions.Add(record);
        }

        var found = new List<ClubRaceDay>();

        foreach (var day in byDay.OrderByDescending(entry => entry.Key))
        {
            var sessions = day.Value;
            var drivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in sessions)
            {
                foreach (var entry in session.Results)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Transponder))
                    {
                        drivers.Add(entry.Transponder);
                    }
                }
            }

            // The day's biggest race, and the latest of those - a club
            // runs its A-main last, and the biggest field is the race the
            // night was about. Not the round's label, because a round
            // three years old may have been deleted since; the session
            // carries its own.
            var headline = sessions
                .OrderByDescending(record => record.Results.Count)
                .ThenByDescending(record => record.EndedAtUtc)
                .First();

            var winner = headline.Results
                .Where(entry => entry.Position == 1)
                .Select(entry => Typed(entry.DriverName))
                .FirstOrDefault(name => name.Length > 0);

            found.Add(new ClubRaceDay(
                Math.Max(1, today.Year - day.Key.Year),
                sessions.Count,
                drivers.Count,
                Typed(headline.Label),
                winner));

            if (found.Count >= Math.Max(1, most))
            {
                break;
            }
        }

        return found;
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
