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
    /// How many drivers' nights a race intro is offered. Two - the figure
    /// CommentaryRaceFacts.TonightFacts states and the reason it gives.
    /// </summary>
    private const int MostNights = CommentaryRaceFacts.TonightFacts;

    /// <summary>
    /// How many finishers' cars a recap is offered.
    ///
    /// Four, where the recap carries two (CommentaryRaceFacts.CarFacts).
    /// The spare two are there because a car line comes back null far more
    /// often than it comes back - most drivers own the car they raced and
    /// have raced one class all season - so reading only the top two would
    /// leave the section empty whenever the leaders' cars had nothing to
    /// say. Four and not the whole field because each one walks the club's
    /// saved results for that driver, and a recap is written while the
    /// next race is being called to the grid.
    /// </summary>
    private const int MostCars = CommentaryRaceFacts.CarFacts * 2;

    /// <summary>
    /// How long a car has to have been in the garage before a driver "has
    /// had" it.
    ///
    /// Two months. A car added at the sign-up desk this evening read out
    /// as "he has had that car in the club's garage since October" is the
    /// app describing its own data entry as club history, which is exactly
    /// the kind of empty line a new club must not be given - see the
    /// thresholds on CommentaryRaceFacts.
    /// </summary>
    private static readonly TimeSpan InGarageLeast = TimeSpan.FromDays(60);

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
    /// How many drivers the conditional's proof will take a grid of.
    ///
    /// Twenty-four, which no club racing meeting reaches - and the number
    /// is not about speed. The proof below runs a full recompute of the
    /// championship for every other entrant in every position still open to
    /// them, which is a square: ten cars is sixty-four recomputes and
    /// twenty-four is four hundred and eighty-four. Both are a few
    /// milliseconds over a season's worth of saved races, once, at staging.
    ///
    /// The cap is here so that a loop on the thread that counts laps is not
    /// something anybody can reach by typing a long roster into a round. It
    /// REFUSES rather than cutting the grid short: a proof over some of the
    /// field is not a proof, and a conditional that was true of eight
    /// drivers and never asked about the ninth is exactly the claim this
    /// whole feature exists to refuse.
    /// </summary>
    private const int MostEntrantsToProve = 24;

    /// <summary>The id the hypothetical finishing order is scored under, which no saved race can have - ChampionshipLive.LiveSessionId's own trick.</summary>
    private const string ConditionalSessionId = "if-they-finish-like-this";

    /// <summary>
    /// THE CHAMPIONSHIP CONDITIONAL: one NAMED finishing order, proved to
    /// change the lead whatever the rest of the field does.
    ///
    /// "If Carl wins this race and Bill finishes second, Carl takes the
    /// lead of the Winter Series from Alex." That claim, or null.
    ///
    /// Null is the usual answer, and every one of the ways it is null is
    /// correct behaviour rather than a gap. See
    /// CommentaryConditionalFacts for why the flat version of this
    /// sentence - "a win here and he leads the championship" - was
    /// deliberately not built on 10 Oct 2026 and must never be.
    ///
    /// -----------------------------------------------------------------
    /// THE PROOF, AND THE SHORTCUT THAT LOOKS RIGHT AND IS NOT
    /// -----------------------------------------------------------------
    ///
    /// The obvious proof is "give every other driver third place and check
    /// they still cannot catch Carl". IT IS WRONG HERE, and the reason is
    /// Season.PointsScheme: it is a user-editable list of position-to-points
    /// rows, and Season.PointsForPosition is a LOOKUP, not a curve. It
    /// returns the row for that place, or zero when there is no row. So:
    ///
    ///   A CLUB CAN TYPE A SCHEME WHERE THIRD PAYS MORE THAN SECOND.
    ///   Nothing sorts the list, nothing validates it, and the Seasons page
    ///   accepts any whole number per row (SeasonsControl's points column
    ///   parses with NumberStyles.Integer, which takes a leading sign, so
    ///   even a NEGATIVE row is reachable without hand-editing the file).
    ///   "The best available position" is therefore not "the most points
    ///   available", and a proof built on the better POSITION proves
    ///   nothing.
    ///
    /// So for every other entrant this takes the MAXIMUM TOTAL over every
    /// position still open to them, each one scored through
    /// SeasonStore.ComputeStandings - the one place in this app that
    /// applies the drop rule. A loop of full recomputes, which the field
    /// sizes make free and which is the only honest way: see
    /// Championship.SwingFor, which recomputes with and without rather than
    /// subtracting and says in its own comment why - "best 5 of 7 can move
    /// a driver's total without this race being the round that dropped,
    /// there is no arithmetic shortcut that survives the drop rule".
    ///
    /// AND RETIRING IS ONE OF THE POSITIONS. A row with no laps is a DNF to
    /// ComputeStandings: no points and no round counted, so the driver
    /// keeps exactly the total they came in with. On a non-negative scheme
    /// that is never better than finishing, so it never matters - but on a
    /// scheme with a negative row it can be the best a driver can do, and a
    /// ceiling that had not considered it would be too low. So each
    /// entrant's ceiling starts at their total BEFORE this race.
    ///
    /// CAN ADDING A ROUND LOWER A TOTAL? On any scheme that pays nothing
    /// worse than zero, no - never, and the three branches of
    /// CountBestRounds are the whole proof. Under the cap the total is the
    /// sum and a non-negative round cannot reduce a sum; at the cap, the new
    /// round either pays no more than the worst kept round (and the total is
    /// unchanged) or it displaces it (and the total rises); over the cap,
    /// the best N of a bigger set is never the smaller. WITH A NEGATIVE ROW
    /// IN THE SCHEME IT CAN: a driver still under the cap who scores minus
    /// five is five points worse off for having raced. That is the exposure
    /// the previous paragraph closes, and it is the reason this reader never
    /// assumes a driver's total can only go up.
    ///
    /// WHY THE NAMED TWO ARE EXACT AND NOT A CEILING. ComputeStandings
    /// re-derives a position from the drivers it actually scored - ghosts
    /// and profiles marked "Exclude from records" are dropped and the field
    /// closes up BEHIND them, and a second row under one name is skipped
    /// and the field closes up over it too. None of that can reach past a
    /// driver placed first or second. So the winner's and the runner-up's
    /// totals are the same number in every arrangement of positions three
    /// and below, which is what makes the sentence sayable at all.
    ///
    /// WHY POSITION THREE IS THE CEILING FOR EVERYBODY ELSE. The condition
    /// names first and second. Given it, no other driver can finish better
    /// than third, so the positions still open to them are three downwards -
    /// which is what the loop walks. Drivers who do not start only make the
    /// field smaller, and a smaller field cannot hand anybody a scoring
    /// place better than third while first and second are taken.
    ///
    /// WHAT IT ASSUMES, STATED BECAUSE IT CANNOT BE PROVED HERE: that the
    /// race is run by the drivers it was staged with. A driver added to the
    /// grid after staging is not in this arithmetic, and cannot be - the
    /// intro is written at staging from the round's own roster. It is the
    /// same assumption ChampionshipLive, Championship.SwingFor and the Big
    /// Screen's "if it ended now" all already make, and the condition's own
    /// wording limits the claim to one finishing order of the race in front
    /// of the room.
    ///
    /// LEVEL ON POINTS IS NOT LEADING, whichever way a tie-break falls -
    /// ChampionshipLive.Headline's judgement, and this matches it. A driver
    /// who can REACH the winner's total, never mind pass it, is enough to
    /// refuse the whole sentence.
    ///
    /// Call this on the UI thread, like everything else here: it reads the
    /// history, the programme and the event stores. Once per race, at
    /// staging, on the same pass that already reads the grid's history, the
    /// stakes and the race's setting.
    /// </summary>
    /// <param name="round">The round about to run. A season's rule takes a race by its round's class and kind, so a freeform race counts towards nothing.</param>
    /// <param name="transponders">Who is on this grid - the round's own roster.</param>
    /// <param name="seasons">The club's seasons, or null where there are none.</param>
    /// <param name="rounds">The programme, for the season's rule.</param>
    /// <param name="events">The race days, for the season's rule.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="profiles">For the driver behind a transponder, the name the announcer says, and who is kept out of records.</param>
    public static RaceConditional? ChampionshipConditional(
        RoundDefinition? round,
        IReadOnlyList<string> transponders,
        SeasonStore? seasons,
        RoundDefinitionStore? rounds,
        RaceEventStore? events,
        SessionHistoryStore history,
        ProfileStore? profiles = null)
    {
        var entrants = Entrants(transponders, profiles);

        // Two to name, and a roster the proof will take. Fewer than two is
        // not a hypothetical; more than MostEntrantsToProve is refused
        // rather than approximated.
        if (entrants.Count < 2 || entrants.Count > MostEntrantsToProve)
        {
            return null;
        }

        var onThisGrid = new HashSet<string>(
            entrants.Select(entrant => entrant.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (season, counted) in ChampionshipLive.Candidates(round, seasons, rounds, events, history))
        {
            // A championship nobody has raced three rounds of yet is a
            // scoreboard rather than a season - see
            // CommentaryConditionalFacts.LeastRoundsCounted for the whole
            // argument. Judged here as well as in the builder so a season
            // that was never going to say anything does not cost the proof.
            if (counted.Count < CommentaryConditionalFacts.LeastRoundsCounted)
            {
                continue;
            }

            var before = SeasonStore.ComputeStandings(season, counted, profiles);

            if (before.Count < 2)
            {
                continue;
            }

            // The two drivers in this race who stand highest, in the
            // table's OWN order - so the tie-breaks ComputeStandings
            // settles are the ones this uses, and there is no second
            // opinion about who is ahead of whom.
            var top = before
                .Where(row => onThisGrid.Contains(row.DriverName))
                .Take(2)
                .ToList();

            // A table this grid has fewer than two drivers in is not what
            // this race is about. The next season might be - the same
            // judgement ChampionshipBefore makes.
            if (top.Count < 2)
            {
                continue;
            }

            // THE CHALLENGER IS NAMED AS THE WINNER, and the driver above
            // them in the table is named as second. That is the ordering
            // that could actually change something, and it is the one the
            // room is already wondering about - "if he beats him today".
            // The other way round is the result that changes nothing worth
            // a sentence.
            //
            // It also means the named winner can never be the championship
            // leader: there is a driver above them in the table by
            // construction. So this feature only ever reports a lead
            // CHANGING HANDS, never a leader extending one, and that is the
            // right side of the choice - a change is news and a status quo
            // is a status report. The guard below keeps the promise anyway,
            // because "by construction" is not a check.
            var second = top[0];
            var challenger = top[1];

            if (string.Equals(challenger.DriverName, second.DriverName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(before[0].DriverName, challenger.DriverName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Prove(season, counted, entrants, onThisGrid, challenger.DriverName, second.DriverName, before, round, profiles)
                is not { } proved)
            {
                continue;
            }

            var winnerName = Sayable(entrants, profiles, challenger.DriverName);
            var secondName = Sayable(entrants, profiles, second.DriverName);

            if (winnerName.Length == 0 || secondName.Length == 0)
            {
                continue;
            }

            return new RaceConditional(
                Typed(season.Name),
                counted.Count,
                TransponderOf(entrants, challenger.DriverName),
                winnerName,
                TransponderOf(entrants, second.DriverName),
                secondName,

                // The total the sentence turns on. Not said - it is in the
                // key, so the same claim is made once and a table that has
                // moved makes a fresh one. See RaceConditional.WinnerPoints.
                proved.WinnerPoints,

                // Who is displaced - and nobody, where the top of the table
                // was LEVEL ON POINTS. ChampionshipLive.Headline treats
                // level as level, and "Carl takes the lead from Alex" said
                // of a table where Alex and Dot were dead level credits
                // Alex with something the standings never gave him, in
                // front of Dot. The builder then says "goes top of" with no
                // name instead, which is true either way.
                before.Count > 1 && before[1].TotalPoints == before[0].TotalPoints
                    ? string.Empty
                    : Sayable(entrants, profiles, before[0].DriverName),

                proved.LeadBy);
        }

        return null;
    }

    /// <summary>
    /// The grid as the standings key on it: one row per DRIVER NAME, with a
    /// transponder each for the theme.
    ///
    /// DEDUPLICATED BY NAME, and that is not tidying - it is what
    /// ComputeStandings itself does. Two rows under one name (a spare
    /// transponder, a car swapped mid-meeting) are scored once there and the
    /// field closes up OVER the skipped row, so a ten-row result with one
    /// repeated name has nine scoring places. A hypothetical built with ten
    /// rows would hand out ten, and every position in the proof below would
    /// be one place out.
    ///
    /// Ghosts are not entrants - a replay of an old lap put on the track for
    /// somebody to chase (GhostService), which ComputeStandings drops and
    /// closes up behind. The same judgement Field and NightStory make.
    ///
    /// A transponder with no profile name behind it is left out: the
    /// standings key on the name, so a driver without one scores under
    /// nothing this can match. No profiles at all means no entrants, which
    /// is the same silence ChampionshipBefore keeps.
    /// </summary>
    private static List<(string Transponder, string Name)> Entrants(
        IReadOnlyList<string> transponders,
        ProfileStore? profiles)
    {
        var found = new List<(string Transponder, string Name)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in transponders)
        {
            var who = Typed(transponder);

            if (who.Length == 0 || GhostService.IsGhost(who))
            {
                continue;
            }

            var name = Typed(profiles?.Get(who)?.DriverName);

            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            found.Add((who, name));
        }

        return found;
    }

    /// <summary>
    /// THE PROOF. Returns the winner's hypothetical total and the margin
    /// where the margin is universal, or null when the claim is not true for
    /// every arrangement of the rest of the field.
    ///
    /// See ChampionshipConditional's remarks for the whole argument,
    /// including the shortcut that looks right and is not. In outline:
    ///
    ///   1. Score the NAMED order through ComputeStandings. The winner's and
    ///      the runner-up's totals come out of that one table and are the
    ///      same in every arrangement, because nothing below third can reach
    ///      past first or second.
    ///
    ///   2. For every OTHER entrant, the maximum total they could reach -
    ///      over every position still open to them and over retiring, each
    ///      scored through ComputeStandings, because the points for a place
    ///      are whatever the club typed against it.
    ///
    ///   3. For everybody NOT in this race, their total as the hypothetical
    ///      table has it, which is the total they came in with: adding a
    ///      race they were not in adds no round for them. Read out of the
    ///      same table as everything else, so there is one table being read
    ///      and no second opinion about who gained what.
    ///
    ///   4. The claim stands only if the winner's total is strictly greater
    ///      than all of those. Level on points is not leading.
    /// </summary>
    private static (int WinnerPoints, int? LeadBy)? Prove(
        Season season,
        IReadOnlyList<SessionRecord> counted,
        IReadOnlyList<(string Transponder, string Name)> entrants,
        IReadOnlySet<string> onThisGrid,
        string winnerName,
        string secondName,
        IReadOnlyList<SeasonStandingEntry> before,
        RoundDefinition? round,
        ProfileStore? profiles)
    {
        if (EntrantNamed(entrants, winnerName) is not { } winner
            || EntrantNamed(entrants, secondName) is not { } second)
        {
            return null;
        }

        var rest = entrants
            .Where(e => !Same(e.Name, winnerName) && !Same(e.Name, secondName))
            .ToList();

        // 1. The named order itself.
        var named = SeasonStore.ComputeStandings(
            season,
            Scored(counted, Hypothetical(round, Order(winner, second, rest))),
            profiles);

        if (Row(named, winnerName) is not { } mine || Row(named, secondName) is not { } theirs)
        {
            // The winner or the runner-up is not in the table the
            // hypothetical produced - a driver ComputeStandings refuses to
            // score, which it is allowed to decide and this is not. No
            // claim to make about them.
            return null;
        }

        // 3. The totals that are FIXED however the rest of the field falls:
        // the named runner-up, and everybody who is not in this race.
        var pinned = theirs.TotalPoints;

        foreach (var row in named)
        {
            if (Same(row.DriverName, winnerName) || onThisGrid.Contains(row.DriverName))
            {
                continue;
            }

            pinned = Math.Max(pinned, row.TotalPoints);
        }

        // 2. And the CEILING of everybody else in this race - the most any
        // one of them could reach. Each one priced over every position
        // still open to them, because the best position is not the most
        // points on a scheme the club typed itself.
        var reachable = int.MinValue;

        foreach (var driver in rest)
        {
            // Retiring. A row with no laps scores nothing and counts no
            // round, so the driver keeps the total they came in with -
            // which on a scheme with a negative row can be the best they
            // can do. See the remarks.
            var most = Points(before, driver.Name);

            for (var slot = 0; slot < rest.Count; slot++)
            {
                var table = SeasonStore.ComputeStandings(
                    season,
                    Scored(counted, Hypothetical(round, Order(winner, second, Moved(rest, driver, slot)))),
                    profiles);

                if (Row(table, driver.Name) is { } row)
                {
                    most = Math.Max(most, row.TotalPoints);
                }
            }

            reachable = Math.Max(reachable, most);
        }

        // 4. LEVEL ON POINTS IS NOT LEADING, so both of these are strict.
        // One driver who can so much as draw with the winner refuses the
        // whole sentence - see ChampionshipLive.Headline, which calls level
        // level whichever way the tie-break falls.
        if (mine.TotalPoints <= pinned)
        {
            return null;
        }

        if (reachable != int.MinValue && mine.TotalPoints <= reachable)
        {
            return null;
        }

        // AND THE MARGIN, only where it is the same margin whatever the
        // rest of the field does.
        //
        // The runner-up's total is pinned. So is every driver who is not in
        // this race. If no other entrant can reach PAST that pinned figure,
        // then the second-placed total in the hypothetical table is that
        // figure in every arrangement, and the gap to it is one number. If
        // one of them can, the margin would be three points on one
        // finishing order and eleven on another, and a figure that moves is
        // the figure a driver disproves after the race - so the line says
        // the lead changes hands and says no number at all.
        return (
            mine.TotalPoints,
            reachable == int.MinValue || reachable <= pinned ? mine.TotalPoints - pinned : null);
    }

    /// <summary>
    /// The hypothetical finishing order as a race the championship can be
    /// asked to score - ChampionshipLive.Live's own trick, which scores the
    /// race on track by appending it to the saved ones.
    ///
    /// ONE LAP EACH, which matters: ComputeStandings reads a row with no
    /// laps as a DNF - on the entry list, never across the line - and gives
    /// it no points and no round. A hypothetical built with zero laps would
    /// prove nothing except that nobody scores anything.
    ///
    /// The round goes on it so a season's rule sees the race it would
    /// actually take, and nothing sets the demo flag or the "[DEMO]" label
    /// that ComputeStandings refuses to score.
    /// </summary>
    /// <summary>
    /// WHAT IS STILL POSSIBLE IN THE CHAMPIONSHIP THIS RACE COUNTS
    /// TOWARDS, over the rounds that remain (12 Oct 2026).
    ///
    /// "With two rounds still to run, only Bill and Sam can win it."
    /// "Bill has already won it - nobody left can reach him." The richest
    /// seam the commentary depth plan named, and the one it listed first;
    /// it sat blocked for two days for want of a single figure, which is
    /// how long a season is. See Season.PlannedRounds.
    ///
    /// HOW EACH HALF IS PROVED, and it is the same method
    /// <see cref="ChampionshipConditional"/> uses because it is the only
    /// one that survives the drop rule. "Best 5 of 7" means a driver's
    /// total is not their points added up, so there is no arithmetic
    /// shortcut: a rival 40 points behind with two rounds left may be
    /// unable to gain 40, because their two worst rounds would drop
    /// instead. The gap and the points available cannot answer it.
    ///
    /// So the remaining rounds are PLAYED OUT at their extremes, through
    /// SeasonStore.ComputeStandings, the one place in this app that applies
    /// the drop rule:
    ///
    ///   for each rival, the rival takes the place that PAYS MOST in every
    ///   remaining round and the leader scores nothing in any of them.
    ///
    /// That is the rival's best case against the leader's worst, so if the
    /// leader still leads it, that rival cannot catch them - for every
    /// arrangement, not most. Each rival is tried on their own, because two
    /// of them cannot both win the same round and pricing them together
    /// would prove something weaker than it looked.
    ///
    /// THE PLACE THAT PAYS MOST, NOT THE WIN. The scoring scheme is a list
    /// the club TYPES, so third paying more than second is a thing people
    /// enter - and then the best finishing POSITION and the most POINTS
    /// AVAILABLE are two different places, and a round built on winning
    /// proves nothing about the total. The sibling reader beside this one
    /// says the same in capitals and this one did not listen for a day.
    /// See Season.BestPayingPosition, and FutureRound for why the places
    /// ahead of the rival have to be filled with cars.
    ///
    /// AND THE LEADER'S WORST IS "NOTHING", which is only true while no row
    /// of the scheme is negative - a round worth zero either leaves a total
    /// alone or is dropped by the best-of rule, so it is the same as not
    /// being there. The moment a row takes points away, a leader can finish
    /// somewhere WORSE than absent, and a proof built on absence would claim
    /// a title that can still be lost. See Season.PenalisesAnyone.
    ///
    /// WHAT IT REFUSES, and the refusals are the feature:
    /// <list type="bullet">
    /// <item>a race counting towards no championship;</item>
    /// <item>a season with no planned length - nothing to remain;</item>
    /// <item>a season already past its planned length, where this race
    /// counts towards a title that is over;</item>
    /// <item>a season whose races do not come to a countable number of
    /// rounds - see <see cref="RoundsCountedSoFar"/>, which is where the
    /// difference between a race and a round lives;</item>
    /// <item>a season too few rounds in to be a season;</item>
    /// <item>a scheme where nothing scores, so no round can change
    /// anything;</item>
    /// <item>a scheme that takes points away, which leaves the leader's
    /// worst case unpriceable;</item>
    /// <item>a scheme whose best-paying place is further down the field
    /// than a hypothetical round can be built out to;</item>
    /// <item>a title more than <see cref="CommentaryStakesFacts.MostStillInIt"/>
    /// drivers can still win, which is most of most seasons and is the
    /// absence of a story rather than one;</item>
    /// <item>a table too long to prove in the time a race intro has.</item>
    /// </list>
    ///
    /// Said in a race INTRO rather than a recap: it is what the race about
    /// to run is worth, which is the thing an intro has least to say about
    /// and the thing a room most wants framed.
    /// </summary>
    /// <param name="round">The round about to run, for the championship it belongs to.</param>
    /// <param name="transponders">Who is on this grid - only used to say names the way the announcer would.</param>
    public static TitleStakes? TitleOnTheLine(
        RoundDefinition? round,
        IReadOnlyList<string> transponders,
        SeasonStore? seasons,
        RoundDefinitionStore? rounds,
        RaceEventStore? events,
        SessionHistoryStore history,
        ProfileStore? profiles = null)
    {
        var entrants = Entrants(transponders, profiles);

        foreach (var (season, counted) in ChampionshipLive.Candidates(round, seasons, rounds, events, history))
        {
            // HOW MANY ROUNDS THOSE RACES COME TO, which is not how many
            // races they are - see RoundsCountedSoFar, which is also the
            // only refusal in this method that was a bug first.
            if (RoundsCountedSoFar(counted, events) is not { } roundsCounted
                || roundsCounted < CommentaryStakesFacts.LeastRoundsCounted)
            {
                continue;
            }

            // THE FIGURE THE CLUB HAD TO TYPE. Null means they have not,
            // and then none of this is sayable - see Season.PlannedRounds.
            if (season.RoundsRemainingAfter(roundsCounted) is not { } left || left < 1)
            {
                continue;
            }

            // A scheme where nothing scores cannot change a table, so
            // "nobody can catch him" would be true and meaningless - and
            // the simulation below would prove it off a scheme the club
            // has effectively switched off.
            //
            // THE PLACE THAT PAYS MOST, which is not necessarily the win:
            // the scheme is a list a club types, so third can pay more than
            // second, and a best case built on the best POSITION is not the
            // most POINTS. The sibling reader beside this one states the
            // same hazard in capitals. See Season.BestPayingPosition.
            var bestPaying = season.BestPayingPosition();

            if (bestPaying < 1)
            {
                continue;
            }

            // A SCHEME THAT TAKES POINTS AWAY has no priceable worst case
            // for the leader, so nothing here can be proved on it: scoring
            // nothing stops being the worst a leader can do the moment a
            // row is negative. See Season.PenalisesAnyone for the whole
            // argument - it is the refusal that keeps "he cannot be caught"
            // off a PA where he can.
            if (season.PenalisesAnyone())
            {
                continue;
            }

            // And a scheme whose best-paying place is further down the
            // field than one hypothetical round can reasonably be built out
            // to - see MostFillersToProve.
            if (bestPaying > MostFillersToProve + 1)
            {
                continue;
            }

            // Held to what can be proved in the time an intro has. Each
            // rival costs one standings recompute over the whole season,
            // and the table is every driver who has scored - not just this
            // grid.
            var before = SeasonStore.ComputeStandings(season, counted, profiles);

            if (before.Count < 2 || before.Count > MostInTableToProve)
            {
                continue;
            }

            var leader = before[0];

            var stillInIt = new List<string> { leader.DriverName };

            foreach (var rival in before.Skip(1))
            {
                if (CanStillCatch(season, counted, left, bestPaying, leader.DriverName, rival.DriverName, profiles))
                {
                    stillInIt.Add(rival.DriverName);

                    // Past the ceiling there is no sentence to build, so
                    // the rest of the table is not worth proving. This is
                    // the ordinary exit two rounds into a nine-round
                    // season.
                    if (stillInIt.Count > CommentaryStakesFacts.MostStillInIt)
                    {
                        break;
                    }
                }
            }

            if (stillInIt.Count > CommentaryStakesFacts.MostStillInIt)
            {
                continue;
            }

            // Said the way the announcer would say them where this grid
            // holds them, and as the standings hold them otherwise - a
            // driver who can still win it need not be in this race.
            string Say(string name) =>
                Sayable(entrants, profiles, name) is { Length: > 0 } said ? said : CommentaryFacts.Clean(name);

            var leaderName = Say(leader.DriverName);

            if (leaderName.Length == 0)
            {
                continue;
            }

            // THE MARGIN, to the best-placed driver who can still WIN it -
            // not to whoever happens to be second in the table.
            //
            // Nearly always the same driver and the same figure. Where they
            // are not, second in the table is somebody this sentence does
            // not name, and the copy reads "leads on 148 points, by 3"
            // directly beside "only Bill and Sam can still win it" - with
            // the three belonging to a driver who cannot. That is reachable
            // rather than theoretical: a rival three points back whose best
            // rounds are already full of maximum scores gains nothing from
            // the rounds that remain, because the drop rule throws the new
            // ones away. It is the same rule that makes every claim in here
            // need playing out instead of adding up.
            //
            // With nobody able to catch the leader there is no contender to
            // measure to, and the copy does not use this figure at all; the
            // gap to second is then the honest thing to carry.
            var chaser = stillInIt.Count > 1 ? stillInIt[1] : before[1].DriverName;

            return new TitleStakes(
                Typed(season.Name),
                roundsCounted,
                left,
                leaderName,
                TransponderOf(entrants, leader.DriverName),
                leader.TotalPoints,
                leader.TotalPoints - Points(before, chaser),
                stillInIt.Count == 1,
                stillInIt.Select(Say).Where(name => name.Length > 0).ToList());
        }

        return null;
    }

    /// <summary>
    /// The most drivers a championship table may hold and still be proved.
    ///
    /// Forty. Each rival costs one recompute of the whole season, so a
    /// club with two hundred names in a table would be asked for two
    /// hundred passes over every race of it in the moment a race intro is
    /// being written. A club with forty drivers in one championship is
    /// already a big club.
    /// </summary>
    private const int MostInTableToProve = 40;

    /// <summary>
    /// Whether one rival can still reach the leader - their BEST case
    /// against the leader's worst, played out through the drop rule.
    ///
    /// The rival takes <paramref name="bestPaying"/> - the place that pays
    /// most on this season's own scheme, which is not necessarily the win -
    /// in every remaining round, and the leader scores nothing in any of
    /// them. If the leader is still ahead after that, this rival cannot
    /// catch them whatever happens. Anything short of playing it out would
    /// be arithmetic over a total that is not a sum, and anything built on
    /// the best POSITION rather than the best POINTS would be arithmetic
    /// over the wrong round - see the remarks on
    /// <see cref="TitleOnTheLine"/>.
    ///
    /// LEVEL COUNTS AS CAUGHT. A rival who can draw level has not been
    /// shut out: where the table settles a tie is the table's business and
    /// not something to claim a championship on.
    /// </summary>
    private static bool CanStillCatch(
        Season season,
        IReadOnlyList<SessionRecord> counted,
        int roundsLeft,
        int bestPaying,
        string leaderName,
        string rivalName,
        ProfileStore? profiles)
    {
        // A rival whose own name looks like one of the filler cars would
        // have the filler's result land on their row and score nothing in
        // the round meant to be their best - see FillerPrefix, which is
        // built so this cannot happen. Unprovable rather than disproved, so
        // it comes back "can still catch": the ceiling on how many drivers
        // may still win it then silences the whole sentence, which is the
        // right way for a proof to fail.
        if (rivalName.StartsWith(FillerPrefix, StringComparison.Ordinal)
            || leaderName.StartsWith(FillerPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        var played = new List<SessionRecord>(counted);

        for (var at = 0; at < roundsLeft; at++)
        {
            // The rival in the place that PAYS most, with the places ahead
            // of them filled by cars that score nothing - the best case
            // being priced. The leader's absence from the round is the
            // leader scoring nothing, which is the other half.
            played.Add(FutureRound(at, rivalName, bestPaying));
        }

        var after = SeasonStore.ComputeStandings(season, played, profiles);

        var rival = Points(after, rivalName);
        var leader = Points(after, leaderName);

        return rival >= leader;
    }

    /// <summary>
    /// The most hypothetical cars one remaining round may be filled out
    /// with to put a driver on the place that pays most.
    ///
    /// Sixty-three, so any scheme whose best-paying place is in the first
    /// sixty-four is provable - which is every scheme a club has typed.
    /// Past it a round would be built with hundreds of cars in it, once per
    /// rival per remaining round, in the moment a race intro is being
    /// written; and a club that has put its biggest score against position
    /// four hundred has not typed a scheme worth reasoning about.
    /// </summary>
    private const int MostFillersToProve = 63;

    /// <summary>
    /// The name the filler cars in a hypothetical round race under - the
    /// places ahead of the driver whose best case is being priced.
    /// </summary>
    /// <remarks>
    /// A CONTROL CHARACTER LEADS IT, and that is the whole point.
    /// SeasonStore.ComputeStandings keys a standings table on the driver's
    /// NAME, without regard to case, and takes one score per driver per
    /// session. A filler sharing a real driver's name would therefore put
    /// its own result on that driver's row - and for the driver being
    /// priced that is fatal: the filler's row comes first, so the rival
    /// would score nothing at all in the round meant to be their best, and
    /// the proof would shut out a rival who is not shut out.
    ///
    /// No name typed into this app can hold a \u0001. The text boxes
    /// cannot produce one, a transponder file cannot carry one, and
    /// CommentaryService.Field turns control characters into spaces before
    /// anything is said. <see cref="CanStillCatch"/> checks anyway, because
    /// "cannot happen" is not a check.
    /// </remarks>
    private const string FillerPrefix = "\u0001" + "open-seat-";

    /// <summary>
    /// A hypothetical round still to come, in which one driver takes the
    /// place that pays most and nobody else scores anything.
    /// </summary>
    /// <remarks>
    /// THE PLACES AHEAD ARE REAL CARS, because position is not read off the
    /// result. ComputeStandings counts the drivers it has scored so far in
    /// the session - which is what makes removing a ghost close the field
    /// up - so a row saying "Position = 4" in a round of one entrant scores
    /// as a WIN. The only way to put a driver fourth is to put three cars
    /// in front of them. A round built the other way proved the best
    /// POSITION and was read as the most POINTS, which on a club's own
    /// typed scheme are two different places.
    ///
    /// AND THOSE CARS COMPLETE NO LAPS, so they take places without taking
    /// points: the position is worked out before the lap check, so a
    /// lapless filler holds its place, scores nothing and counts no round.
    /// They land on rows of their own in the hypothetical table, which
    /// nothing reads - see <see cref="FillerPrefix"/> for why those rows
    /// cannot be a real driver's.
    ///
    /// ONE LAP for the driver being priced, which matters: ComputeStandings
    /// reads a row with no laps as a DNF - on the entry list, never across
    /// the line - and gives it no points and no round.
    ///
    /// ITS OWN ID PER ROUND, which <see cref="Hypothetical"/>'s single
    /// ConditionalSessionId could not give: several of these go into one
    /// list, and two sessions sharing an id is a season counting one round
    /// where it was handed three.
    ///
    /// No RoundId, because these rounds do not exist. EventSpine has
    /// already chosen the races that count by the time this is added to
    /// them, so nothing downstream asks.
    /// </remarks>
    private static SessionRecord FutureRound(int at, string driverName, int position)
    {
        var place = Math.Clamp(position, 1, MostFillersToProve + 1);

        var results = new List<SessionResultEntry>(place);

        for (var ahead = 1; ahead < place; ahead++)
        {
            results.Add(new SessionResultEntry
            {
                Transponder = string.Empty,
                DriverName = $"{FillerPrefix}{at}-{ahead}",
                Position = ahead,
                Laps = 0,
            });
        }

        results.Add(new SessionResultEntry
        {
            Transponder = driverName,
            DriverName = driverName,
            Position = place,
            Laps = 1,
        });

        return new SessionRecord
        {
            Id = $"if-the-season-runs-out-{at}",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = DateTime.UtcNow.AddDays(at + 1),
            Results = results,
        };
    }

    /// <summary>
    /// How many ROUNDS of a championship the races counting towards it come
    /// to, or null when they do not come to a count this app can stand
    /// behind.
    ///
    /// WHY IT IS NOT A COUNT OF RACES, which is what this was for half a day
    /// and was wrong. A club's "round" is a race DAY - eight rounds of a
    /// winter series is eight meetings - and the things counting towards a
    /// championship are SESSIONS. One meeting can hand a championship more
    /// than one: an A-main and a B-main both carry RoundKind.Main, and a
    /// championship with no class named counts every class's main on the
    /// night. Counting races as rounds then has a six-round season already
    /// nine rounds in, so nothing remains, so a rival with two meetings
    /// left to catch somebody in is told over a PA that they cannot - the
    /// exact false claim this whole file exists in order not to make.
    ///
    /// THE DAYS ARE COUNTED instead, through the rounds those races were
    /// filed under: RaceEvent.RoundIds is what makes a round part of a
    /// meeting.
    ///
    /// AND IT REFUSES unless they come to exactly one race per day, which is
    /// the shape the proof downstream is built for - it plays each remaining
    /// round out as ONE race. A championship counting two races a meeting
    /// has a best case worth two races a meeting, and pricing it at one
    /// under-rates every rival: the same false shut-out by the other road.
    /// Nearly every club is already the provable shape, because MainsOnly
    /// and a named class give it to them.
    ///
    /// Null, too, for a race this app cannot place on a day at all. A
    /// session ticked into a championship by hand carries no round, and a
    /// championship holding one has a round count that is a guess.
    /// </summary>
    private static int? RoundsCountedSoFar(
        IReadOnlyList<SessionRecord> counted,
        RaceEventStore? events)
    {
        if (events is null || counted.Count == 0)
        {
            return null;
        }

        var days = new HashSet<string>(StringComparer.Ordinal);

        foreach (var session in counted)
        {
            // EventForRound rather than a lookup built here: which day a
            // round belongs to is RaceEventStore's own question and it is
            // already the one EventSpine.RuleAccepts asks.
            if (session.RoundId is not { Length: > 0 } roundId
                || events.EventForRound(roundId) is not { } day
                || !days.Add(day.Id))
            {
                return null;
            }
        }

        return days.Count;
    }

    private static SessionRecord Hypothetical(
        RoundDefinition? round,
        IReadOnlyList<(string Transponder, string Name)> order) => new()
        {
            Id = ConditionalSessionId,
            SessionType = RaceSessionType.Race,
            RoundId = round?.Id,
            StartedAtUtc = DateTime.UtcNow,
            Results = order
                .Select((entrant, index) => new SessionResultEntry
                {
                    Transponder = entrant.Transponder,
                    DriverName = entrant.Name,
                    Position = index + 1,
                    Laps = 1,
                })
                .ToList(),
        };

    /// <summary>The named two, then everybody else - the order the hypothetical is built in.</summary>
    private static List<(string Transponder, string Name)> Order(
        (string Transponder, string Name) winner,
        (string Transponder, string Name) second,
        IReadOnlyList<(string Transponder, string Name)> rest)
    {
        var order = new List<(string Transponder, string Name)>(rest.Count + 2) { winner, second };

        order.AddRange(rest);

        return order;
    }

    /// <summary><paramref name="rest"/> with one driver taken out and put back at <paramref name="slot"/> - one position still open to them.</summary>
    private static List<(string Transponder, string Name)> Moved(
        IReadOnlyList<(string Transponder, string Name)> rest,
        (string Transponder, string Name) driver,
        int slot)
    {
        var order = rest.Where(e => !Same(e.Name, driver.Name)).ToList();

        order.Insert(Math.Clamp(slot, 0, order.Count), driver);

        return order;
    }

    /// <summary>The championship's saved races with the hypothetical one on the end.</summary>
    private static List<SessionRecord> Scored(IReadOnlyList<SessionRecord> counted, SessionRecord hypothetical)
    {
        var races = new List<SessionRecord>(counted.Count + 1);

        races.AddRange(counted);
        races.Add(hypothetical);

        return races;
    }

    /// <summary>One driver's row in a standings table, by the name it keys on, or null.</summary>
    private static SeasonStandingEntry? Row(IReadOnlyList<SeasonStandingEntry> table, string name) =>
        table.FirstOrDefault(entry => Same(entry.DriverName, name));

    /// <summary>One driver's total in a standings table - zero where the table does not hold them, which is what they have.</summary>
    private static int Points(IReadOnlyList<SeasonStandingEntry> table, string name) =>
        Row(table, name)?.TotalPoints ?? 0;

    /// <summary>The same name, the way ComputeStandings compares them.</summary>
    private static bool Same(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A standings name as the ANNOUNCER would say it, where this grid has
    /// the driver behind it - and the standings' own name otherwise.
    ///
    /// The announcer's name first, for the reason NightStory gives: a model
    /// handed two names for one driver writes about two drivers, and the
    /// grid in this same prompt is listed by SpokenName. The fallback is the
    /// standings name because the driver displaced by the hypothetical need
    /// not be racing tonight at all, and then there is no profile on this
    /// grid to ask - the same place ChampionshipAtStake's leader comes from.
    /// </summary>
    private static string Sayable(
        IReadOnlyList<(string Transponder, string Name)> entrants,
        ProfileStore? profiles,
        string standingsName)
    {
        var transponder = TransponderOf(entrants, standingsName);

        if (profiles is not null && transponder.Length > 0 && SaidAs(profiles, transponder) is { Length: > 0 } said)
        {
            return said;
        }

        return Typed(standingsName);
    }

    /// <summary>The transponder this grid carries a standings name under, or empty where the driver is not in this race.</summary>
    private static string TransponderOf(
        IReadOnlyList<(string Transponder, string Name)> entrants,
        string standingsName) =>
        EntrantNamed(entrants, standingsName) is { } entrant ? entrant.Transponder : string.Empty;

    /// <summary>
    /// The grid's row for one standings name, or null where the driver is
    /// not in this race.
    ///
    /// A loop rather than FirstOrDefault because the row is a value tuple,
    /// and the default of one is two nulls in fields the compiler has been
    /// told cannot be null - which is a trap for whoever reads this next
    /// rather than a bug today.
    /// </summary>
    private static (string Transponder, string Name)? EntrantNamed(
        IReadOnlyList<(string Transponder, string Name)> entrants,
        string standingsName)
    {
        foreach (var entrant in entrants)
        {
            if (Same(entrant.Name, standingsName))
            {
                return entrant;
            }
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
    /// The outright lap record on the layout a race is being run on - the
    /// venue's own half of the depth.
    ///
    /// THE TRACK PLAN'S FIGURE, NOT A SECOND WALK OF THE LAP TIMES. See
    /// VenueRecord for the whole argument: the club has already seen this
    /// number on the Track Layouts page, the Big Screen and its own
    /// published results, and a reader here that reached a different answer
    /// would be the announcer contradicting the club's own screens. The
    /// store that keeps it already refuses a demo lap, a practice lap, a
    /// ghost, a rental and a driver flagged out of records.
    ///
    /// WHAT THE SAVED RACES ARE STILL FOR
    ///
    /// Two things the plan cannot tell: how many races the club has
    /// actually run on this layout - a plan drawn this afternoon and used
    /// once holds a "record" that is the only lap on it - and how many have
    /// run since the record was set, which is what "nobody has beaten it
    /// since" counts. Both leave out demo and practice sessions, the rule
    /// <see cref="RacesIn"/> states.
    ///
    /// Null four ways, each of them the honest answer: no layout named, no
    /// layout by that name, no record on it, and a record with no date -
    /// which cannot say when it was set and, worse, cannot be told apart
    /// from one set an hour ago.
    ///
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="layouts">The club's track plans. Null at a club that has none.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="layoutName">The layout being raced on, as the round or the session names it.</param>
    /// <param name="nowLocal">Now, in local time - for which night counts as tonight, and which year is this one. Defaults to now.</param>
    public static VenueRecord? LayoutRecord(
        TrackLayoutStore? layouts,
        SessionHistoryStore history,
        string? layoutName,
        DateTime? nowLocal = null)
    {
        var wanted = Typed(layoutName);

        if (layouts is null || wanted.Length == 0)
        {
            return null;
        }

        // Found by NAME and without regard to case, which is how every
        // other caller in the app finds a layout (the Race Control picker
        // lists names; TrackLayoutStore.IsNameTaken compares them this
        // way). A round carries the name, not the id.
        var layout = layouts.All.FirstOrDefault(plan =>
            string.Equals(Typed(plan.Name), wanted, StringComparison.OrdinalIgnoreCase));

        if (layout is null || layout.RecordLapMs <= 0 || layout.RecordSetUtc is not { } setUtc)
        {
            return null;
        }

        var today = nowLocal ?? DateTime.Now;

        var onLayout = 0;
        var since = 0;

        foreach (var record in history.All)
        {
            if (record.SessionType == RaceSessionType.Practice
                || record.IsDemoSession()
                || !string.Equals(Typed(record.TrackLayoutName), wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            onLayout++;

            if (record.EndedAtUtc > setUtc)
            {
                since++;
            }
        }

        return new VenueRecord(
            Typed(layout.Name),
            layout.RecordLapMs / 1000.0,
            Typed(layout.RecordHolderName),
            Month(setUtc, today),
            onLayout,
            since,

            // The same 6am-to-6am night the rest of the app counts by
            // (TonightAtTheTrack.NightOf), so a club night that runs past
            // midnight is one night and a record set at ten past twelve is
            // still tonight's rather than yesterday's history.
            TonightAtTheTrack.NightOf(setUtc.ToLocalTime()) == TonightAtTheTrack.NightOf(today));
    }

    /// <summary>
    /// The shape of these drivers' last few race NIGHTS in a class - a run
    /// of podium nights, perfect attendance, a clean run of finishes, or a
    /// driver back after a while away.
    ///
    /// WHY NIGHTS AND NOT RACES (11 Oct 2026)
    ///
    /// <see cref="GridHistory"/> already counts a driver's recent placings,
    /// and CommentaryHistoryFacts.FormGuide reads them out in order -
    /// "fourth, second, first, sixth". That is a results sheet read aloud.
    /// A club member's memory is organised by NIGHTS: "he has been on the
    /// podium three weeks running" is a thing a regular says to another
    /// regular in the pits, and it is what the depth plan asked for and did
    /// not get. See CommentaryWeekFacts.
    ///
    /// Counting nights is also why DriverForm could not be extended to do
    /// it. A night is a DATE, and the shape needs the nights a driver was
    /// ABSENT from as much as the ones they raced; DriverForm carries
    /// counts and a list of placings with no window on it, so a run of
    /// three podiums in it could be three weeks or three years and a
    /// driver who missed a month looks identical to one who did not.
    ///
    /// THE WINDOW IS THE CLUB'S OWN NIGHTS, never a span of calendar
    /// weeks. A club that skipped a fortnight for a holiday has not had a
    /// driver miss two weeks, and a club that races fortnightly is not
    /// owed half the lines. The last
    /// <see cref="CommentaryWeekFacts.NightsInWindow"/> nights the club
    /// actually raced this class is the whole of it.
    ///
    /// A NIGHT IS THE 6AM-TO-6AM NIGHT the rest of the app counts by
    /// (TonightAtTheTrack.NightOf), so a club night running past midnight
    /// is one night rather than two.
    ///
    /// TONIGHT IS LEFT OUT. Every line is about what a driver brought with
    /// them, and a run of three that includes the heat an hour ago is the
    /// app describing this evening as history in front of the room that
    /// watched it. The same rule VenueRecord.SetTonight states.
    ///
    /// Empty when there is no programme to scope a class by, no filed
    /// nights, or nobody with enough of them. Call it on the UI thread,
    /// like everything else here.
    /// </summary>
    /// <param name="transponders">Who is in the race. Only these drivers are worked out.</param>
    /// <param name="className">Whose nights to count. Blank counts every race the club has run.</param>
    /// <param name="profiles">The club's profiles - for the announcer's name for a driver.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="rounds">The club's rounds, for scoping to a class. A class cannot be scoped without them.</param>
    /// <param name="nowLocal">Now, in local time - for which night counts as tonight and which year is this one. Defaults to now.</param>
    public static IReadOnlyList<DriverWeeks> RecentWeeks(
        IReadOnlyList<string>? transponders,
        string? className,
        ProfileStore? profiles,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        DateTime? nowLocal = null)
    {
        var found = new List<DriverWeeks>();

        if (profiles is null || transponders is null || transponders.Count == 0)
        {
            return found;
        }

        var today = nowLocal ?? DateTime.Now;
        var tonight = TonightAtTheTrack.NightOf(today);

        // The class's races, newest first, with practice and demo already
        // left out - the rule RacesIn states.
        var races = RacesIn(className, history, rounds);

        // Grouped into the club's own race NIGHTS, newest night first.
        // A list of (night, that night's races) rather than a dictionary,
        // because the ORDER is the whole point: a run is counted backwards
        // from the most recent night.
        var nights = new List<(DateTime Night, List<SessionRecord> Races)>();

        foreach (var race in races)
        {
            var night = TonightAtTheTrack.NightOf(race.EndedAtUtc.ToLocalTime());

            // Tonight is not history. See the remarks.
            if (night == tonight)
            {
                continue;
            }

            if (nights.Count > 0 && nights[^1].Night == night)
            {
                nights[^1].Races.Add(race);

                continue;
            }

            if (nights.Count >= CommentaryWeekFacts.NightsInWindow)
            {
                break;
            }

            nights.Add((night, new List<SessionRecord> { race }));
        }

        if (nights.Count == 0)
        {
            return found;
        }

        foreach (var transponder in transponders)
        {
            if (WeeksOf(nights, profiles, transponder, today) is { } weeks)
            {
                found.Add(weeks);
            }
        }

        return found;
    }

    /// <summary>
    /// One driver's shape over these nights, newest night first - or null
    /// when they have no sayable name or were not out at any of them.
    ///
    /// The thresholds are the BUILDER's, not this reader's: every figure is
    /// handed over so CommentaryWeekFacts can refuse it and say why, which
    /// is how every other figure in here works.
    /// </summary>
    private static DriverWeeks? WeeksOf(
        IReadOnlyList<(DateTime Night, List<SessionRecord> Races)> nights,
        ProfileStore profiles,
        string transponder,
        DateTime nowLocal)
    {
        var who = Typed(transponder);

        if (who.Length == 0)
        {
            return null;
        }

        var name = SaidAs(profiles, who);

        if (name.Length == 0)
        {
            return null;
        }

        var wasOut = 0;
        var started = 0;
        var finishedAll = true;
        var run = 0;
        var runLive = true;
        var lastOut = string.Empty;

        foreach (var (_, racesThatNight) in nights)
        {
            var bestThatNight = 0;
            var raced = false;

            foreach (var race in racesThatNight)
            {
                foreach (var entry in race.Results)
                {
                    if (!string.Equals(Typed(entry.Transponder), who, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    raced = true;
                    started++;

                    // A retirement or a disqualification is not a finish,
                    // and is what "finished every one of them" has to be
                    // false for.
                    if (Typed(entry.Outcome).Length > 0)
                    {
                        finishedAll = false;
                    }
                    else if (entry.Position > 0 && (bestThatNight == 0 || entry.Position < bestThatNight))
                    {
                        bestThatNight = entry.Position;
                    }

                    break;
                }
            }

            if (!raced)
            {
                // A NIGHT THEY MISSED NEITHER COUNTS NOR BREAKS A RUN. A
                // driver away for a fortnight has not had his podium run
                // ended by being on holiday, and a room that was there
                // knows it. See CommentaryWeekFacts.
                continue;
            }

            wasOut++;

            if (lastOut.Length == 0)
            {
                // The newest night they were out at, which is what "their
                // last was" means. Dated off that night's last race.
                lastOut = When(racesThatNight[^1], nowLocal);
            }

            if (!runLive)
            {
                continue;
            }

            if (bestThatNight is > 0 and <= PodiumPlaces)
            {
                run++;
            }
            else
            {
                // A night they WERE there for and did not manage it. This
                // is the only thing that ends a run.
                runLive = false;
            }
        }

        if (wasOut == 0)
        {
            return null;
        }

        return new DriverWeeks(
            who,
            name,
            nights.Count,
            wasOut,
            nights.Count - wasOut,
            run,
            started,
            finishedAll,
            lastOut);
    }

    /// <summary>
    /// What counts as a podium. Three, as everywhere else - and said as
    /// "the top three" in the line rather than "a podium", because a club
    /// running six-car heats does not hand out trophies for them.
    /// </summary>
    private const int PodiumPlaces = 3;

    /// <summary>
    /// What the club's whole filed history says about the layout a race is
    /// being run on, beyond the one outright record
    /// <see cref="LayoutRecord"/> carries: every driver on this grid's own
    /// best lap here, and who has been winning here lately.
    ///
    /// WHY THIS IS WORTH WALKING THE HISTORY FOR (11 Oct 2026)
    ///
    /// The outright record belongs to one driver, and a club of forty hears
    /// it as a fact about somebody else. A driver's own best on a layout
    /// belongs to whoever is being talked about - the eighth-place regular
    /// has one too - and no human announcer can hold forty drivers' bests
    /// across four layouts in his head. It is the cheapest line in the
    /// broadcast that is about the listener.
    ///
    /// WHY NOT DriverStats, AND WHY NOT AchievementEvaluator
    ///
    /// DriverStats counts a driver across everything they race and knows
    /// nothing about layouts. AchievementEvaluator's BestEverOn is the
    /// layout's OUTRIGHT best across all drivers, which is the record this
    /// is explicitly not. What IS reused is the one rule that must not be
    /// written twice - whose laps may stand as a record at all - because a
    /// reader that let a ghost's or a rental's lap stand as a driver's best
    /// would put a number on the PA that the club's own Track Layouts page
    /// contradicts. See AchievementEvaluator.CanHoldTrackRecord.
    ///
    /// RACES, AND ONLY RACES. Practice sessions have no result anybody
    /// would call a finish and a demo race's laps are invented, so both are
    /// left out - the rule <see cref="RacesIn"/> states and this honours
    /// without needing a programme, because a layout is not a class.
    ///
    /// TONIGHT IS LEFT OUT ENTIRELY. "His best ever here is 11.402" said
    /// about a lap he set in the heat an hour ago is the timing system
    /// describing this evening as club history, in front of the man who did
    /// it - the same fault VenueRecord.SetTonight exists to prevent. So the
    /// figure is what he had to beat when he arrived, which is also the only
    /// version of it that is worth hearing before a race.
    ///
    /// Null when there is no layout named or nothing has been filed on it.
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="profiles">The club's profiles - for the announcer's name for a driver, and for whose laps may stand.</param>
    /// <param name="layoutName">The layout being raced on, as the round or the session names it.</param>
    /// <param name="transponders">Who is in the race. Their own bests are the only ones worked out.</param>
    /// <param name="nowLocal">Now, in local time - for which night counts as tonight and which year is this one. Defaults to now.</param>
    public static LayoutLegend? LayoutStory(
        SessionHistoryStore history,
        ProfileStore? profiles,
        string? layoutName,
        IReadOnlyList<string>? transponders,
        DateTime? nowLocal = null)
    {
        var wanted = Typed(layoutName);

        if (wanted.Length == 0)
        {
            return null;
        }

        var today = nowLocal ?? DateTime.Now;
        var tonight = TonightAtTheTrack.NightOf(today);

        // Every RACE the club has filed on this layout before tonight,
        // oldest first - the order the run below is counted in.
        var onLayout = new List<SessionRecord>();

        foreach (var session in history.All)
        {
            if (session.SessionType == RaceSessionType.Practice
                || session.IsDemoSession()
                || !string.Equals(Typed(session.TrackLayoutName), wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Tonight's own races are not history yet. See the remarks.
            if (TonightAtTheTrack.NightOf(session.EndedAtUtc.ToLocalTime()) == tonight)
            {
                continue;
            }

            onLayout.Add(session);
        }

        if (onLayout.Count == 0)
        {
            return null;
        }

        onLayout.Sort((a, b) => a.EndedAtUtc.CompareTo(b.EndedAtUtc));

        var bests = new List<DriverOnLayout>();

        foreach (var transponder in transponders ?? Array.Empty<string>())
        {
            if (OwnBestOn(onLayout, profiles, transponder, today) is { } best)
            {
                bests.Add(best);
            }
        }

        // Quickest first, so an intro that can only carry two carries the
        // two figures worth stating rather than the first two it was
        // handed. ForTheLayout then decides how many survive.
        bests.Sort((a, b) => a.BestSeconds.CompareTo(b.BestSeconds));

        var (winner, run) = RunOfWinsOn(onLayout, profiles);

        return new LayoutLegend(Typed(onLayout[^1].TrackLayoutName), onLayout.Count, bests, winner, run);
    }

    /// <summary>
    /// One driver's quickest lap in these races, with the month they set it
    /// and how many of the races they were in - or null when they have
    /// neither a sayable name, a lap, nor enough races here to make the
    /// figure their measure of the place.
    ///
    /// The threshold itself is the BUILDER's
    /// (CommentaryRaceFacts.LeastRacesForOwnBest), not this reader's: the
    /// count is handed over so the builder can refuse it and say why, which
    /// is how every other figure in here works.
    /// </summary>
    private static DriverOnLayout? OwnBestOn(
        IReadOnlyList<SessionRecord> onLayout,
        ProfileStore? profiles,
        string transponder,
        DateTime nowLocal)
    {
        var who = Typed(transponder);

        if (who.Length == 0 || profiles is null)
        {
            return null;
        }

        // The announcer's own name for them, never the transponder and
        // never the name on an old result - a transponder changes hands
        // between seasons. See SaidAs.
        var name = SaidAs(profiles, who);

        if (name.Length == 0)
        {
            return null;
        }

        // Whose laps may stand at all. A ghost's or a rental's lap left in
        // an old saved result would otherwise become this driver's "best
        // ever here" - a number the club's own Track Layouts page does not
        // recognise. The same rule the live record keeps.
        if (!AchievementEvaluator.CanHoldTrackRecord(who, profiles))
        {
            return null;
        }

        var bestMs = 0L;
        var setAtUtc = default(DateTime);
        var races = 0;

        foreach (var session in onLayout)
        {
            foreach (var entry in session.Results)
            {
                if (!string.Equals(Typed(entry.Transponder), who, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                races++;

                if (entry.FastLapMs > 0 && (bestMs == 0 || entry.FastLapMs < bestMs))
                {
                    bestMs = entry.FastLapMs;
                    setAtUtc = session.EndedAtUtc;
                }

                // One row per driver per session; a second would double
                // the race count.
                break;
            }
        }

        if (races == 0 || bestMs <= 0)
        {
            return null;
        }

        return new DriverOnLayout(who, name, bestMs / 1000.0, Month(setAtUtc, nowLocal), races);
    }

    /// <summary>
    /// The unbroken run of one driver winning every one of the club's most
    /// recent races on this layout, newest backwards - or no name and zero,
    /// which is the usual answer at a club where the wins go round.
    ///
    /// A WIN NEEDS SOMEBODY TO BEAT. A race with fewer than
    /// <see cref="LeastCarsForAWin"/> finishers is not counted as a win and
    /// does not break a run either: it is passed over, because a two-car
    /// heat somebody turned up alone for should neither make a run nor end
    /// one. A race with no identifiable winner is treated the same way.
    ///
    /// Counted by TRANSPONDER and named from today's profile, like
    /// everything else that holds a run to a person - see SaidAs.
    /// </summary>
    private static (string Winner, int Run) RunOfWinsOn(
        IReadOnlyList<SessionRecord> onLayout,
        ProfileStore? profiles)
    {
        if (profiles is null)
        {
            return (string.Empty, 0);
        }

        var holder = string.Empty;
        var run = 0;

        for (var at = onLayout.Count - 1; at >= 0; at--)
        {
            var winner = WinnerIn(onLayout[at], profiles);

            if (winner.Length == 0)
            {
                // Nothing to count and nothing to break. See the remarks.
                continue;
            }

            if (holder.Length == 0)
            {
                holder = winner;
                run = 1;
                continue;
            }

            if (!string.Equals(holder, winner, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            run++;
        }

        if (run == 0)
        {
            return (string.Empty, 0);
        }

        return (SaidAs(profiles, holder), run);
    }

    /// <summary>
    /// Who won this race, as a transponder - or empty when nobody did in a
    /// way worth counting: too few cars out, no first place recorded, or a
    /// first place whose laps may not stand.
    /// </summary>
    private static string WinnerIn(SessionRecord session, ProfileStore profiles)
    {
        var cars = 0;
        var first = string.Empty;

        foreach (var entry in session.Results)
        {
            var who = Typed(entry.Transponder);

            if (who.Length == 0 || !AchievementEvaluator.CanHoldTrackRecord(who, profiles))
            {
                continue;
            }

            cars++;

            // Position 1 and an actual finish. A DNF or a DQ placed first
            // by an old file is not a win.
            if (entry.Position == 1 && Typed(entry.Outcome).Length == 0)
            {
                first = who;
            }
        }

        return cars >= LeastCarsForAWin ? first : string.Empty;
    }

    /// <summary>
    /// How many real cars have to be out for the race to count as a win on
    /// a layout.
    ///
    /// Three. "The last five races here have gone to Bill" said about five
    /// evenings Bill turned up to on his own is true and is the kind of
    /// true a club laughs at - and it is the superlative with nothing
    /// behind it that every threshold in CommentaryRaceFacts exists to
    /// avoid.
    /// </summary>
    private const int LeastCarsForAWin = 3;

    /// <summary>
    /// Who has been winning this class's MAINS on this layout, counted in
    /// MEETINGS rather than races - the one claim in this app that no human
    /// announcer could make.
    ///
    /// WHY THIS IS NOT <see cref="LayoutStory"/>'s run (11 Oct 2026)
    ///
    /// LayoutStory counts an unbroken run of wins over every race filed on
    /// a layout, of any kind, in any class. That is a true figure and
    /// WhoWinsHere says it carefully - but it is not the line a club
    /// actually wants, which is about the race that matters:
    ///
    ///   "Bill had won the main on this layout at each of the last
    ///    fourteen meetings. Tonight that run ends."
    ///
    /// Three things have to be different for that sentence to be true.
    /// MAINS ONLY, because a run that counts heats is a run of heats.
    /// ONE CLASS, because a club with three classes has three mains a
    /// night and a run across all of them is a run of nothing. And
    /// MEETINGS, not races, because a club that runs an A and a B main
    /// would otherwise have one evening count twice.
    ///
    /// ONE MAIN PER MEETING, and it is the top one: the lowest
    /// <see cref="RoundDefinition.MainTier"/> filed that night, latest
    /// first on a tie. A B-main winner must not end the A-main's run.
    ///
    /// TONIGHT IS LEFT OUT, like LayoutStory - this is the bar the night's
    /// own result is held against, so counting the result into the bar
    /// would make every winner their own precedent. The caller passes
    /// tonight's winner to the fact builder; see
    /// CommentaryRaceFacts.ForTheMainsHere.
    ///
    /// Null every way of having nothing: no layout named, no class named,
    /// no programme to tell a main from a heat, nothing filed, or nobody
    /// with a run. A club with no Program tab filled in has no mains to
    /// count and gets silence rather than a guess.
    ///
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="rounds">The programme - the only thing that knows a main from a heat, and which class each was.</param>
    /// <param name="profiles">For the announcer's name for a driver, and for whose wins may stand.</param>
    /// <param name="layoutName">The layout being raced on, as the round or the session names it.</param>
    /// <param name="className">The class whose mains are counted. Blank is no answer, not "every class".</param>
    /// <param name="nowLocal">Now, in local time - for which night counts as tonight. Defaults to now.</param>
    public static MainsOnLayout? MainsHere(
        SessionHistoryStore history,
        RoundDefinitionStore? rounds,
        ProfileStore? profiles,
        string? layoutName,
        string? className,
        DateTime? nowLocal = null)
    {
        var layout = Typed(layoutName);
        var cls = Typed(className);

        if (rounds is null || profiles is null || layout.Length == 0 || cls.Length == 0)
        {
            return null;
        }

        // Which filed rounds are this class's MAINS, and what tier each is.
        // By id, which is what a session carries - a session knows nothing
        // about classes or round kinds on its own.
        var tiers = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var round in rounds.All)
        {
            if (round.Kind != RoundKind.Main
                || string.IsNullOrWhiteSpace(round.Id)
                || !string.Equals(Typed(round.ClassName), cls, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // An untiered main is the top one - a club that runs a single
            // final and never set a tier on it has run its A-main. The
            // same reading RaceStature takes.
            tiers[round.Id] = round.MainTier <= 0 ? 0 : round.MainTier;
        }

        if (tiers.Count == 0)
        {
            return null;
        }

        var today = nowLocal ?? DateTime.Now;
        var tonight = TonightAtTheTrack.NightOf(today);

        // The top main of each meeting, by meeting. The 6am-to-6am night
        // the rest of the app counts by, so a club night that runs past
        // midnight is one meeting.
        var topOfEachMeeting = new Dictionary<DateTime, (SessionRecord Session, int Tier)>();

        foreach (var session in history.All)
        {
            if (session.SessionType == RaceSessionType.Practice
                || session.IsDemoSession()
                || session.RoundId is not { } id
                || !tiers.TryGetValue(id, out var tier)
                || !string.Equals(Typed(session.TrackLayoutName), layout, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var night = TonightAtTheTrack.NightOf(session.EndedAtUtc.ToLocalTime());

            // Tonight's own races are the thing being held against this,
            // not part of it. See the remarks.
            if (night == tonight)
            {
                continue;
            }

            if (!topOfEachMeeting.TryGetValue(night, out var already)
                || tier < already.Tier
                || (tier == already.Tier && session.EndedAtUtc > already.Session.EndedAtUtc))
            {
                topOfEachMeeting[night] = (session, tier);
            }
        }

        if (topOfEachMeeting.Count == 0)
        {
            return null;
        }

        // Newest meeting first, which is the direction a run is counted in.
        var meetings = topOfEachMeeting
            .OrderByDescending(one => one.Key)
            .Select(one => one.Value.Session)
            .ToList();

        var holder = string.Empty;
        var run = 0;

        foreach (var session in meetings)
        {
            var winner = WinnerIn(session, profiles);

            if (winner.Length == 0)
            {
                // Nothing to count and nothing to break - the same rule
                // RunOfWinsOn states, and for the same reason.
                continue;
            }

            if (holder.Length == 0)
            {
                holder = winner;
                run = 1;
                continue;
            }

            if (!string.Equals(holder, winner, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            run++;
        }

        if (run == 0)
        {
            return null;
        }

        return new MainsOnLayout(
            layout,
            cls,
            meetings.Count,
            SaidAs(profiles, holder),
            holder,
            run);
    }

    /// <summary>
    /// How strong the field in a race is, against the club's own season -
    /// the line a race intro has least of and wants most.
    ///
    /// WHY TONIGHT'S OWN RACES ARE NOT WHAT TONIGHT IS COMPARED WITH
    ///
    /// A club runs six heats with the same ten entries. Compared against
    /// every filed race, every heat after the first is "the biggest field
    /// since the first heat" - a sentence about the programme rather than
    /// about the season, said five times. So the search for a field this
    /// big skips tonight, while still COUNTING tonight's heats as races
    /// that have run since: both halves are true, and together they are
    /// the claim a club can check.
    ///
    /// WHAT "WON A MAIN THIS YEAR" COUNTS
    ///
    /// A first place in a race filed against a round of kind Main, in this
    /// class, at this club, with its local date in this calendar year.
    /// Every one of those four is in the line, because dropping any one of
    /// them makes it false for somebody: a win in another class, a win in
    /// a heat, a win four seasons ago.
    ///
    /// Null when there is no class to scope it to and when nobody is on
    /// the grid; thin rather than null at a club with little on file, and
    /// the builders refuse it there - see CommentaryRaceFacts.
    ///
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="transponders">Every entry in the race - the whole roster, not the few an intro names. See FieldStrength.</param>
    /// <param name="className">Whose races to count. Blank is no answer rather than every class: a field has no size without a class to size it in.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="rounds">Which round belongs to which class, and which rounds are mains.</param>
    /// <param name="nowLocal">Now, in local time. Defaults to now.</param>
    public static FieldStrength? Field(
        IReadOnlyList<string> transponders,
        string? className,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        DateTime? nowLocal = null)
    {
        var wanted = Typed(className);

        if (wanted.Length == 0)
        {
            return null;
        }

        // Ghosts are not entries. A ghost is a replay of an old lap put on
        // the track for somebody to chase (GhostService), so counting one
        // would overstate the field - and "the biggest entry this class has
        // had" is a claim about cars with people behind them.
        var entries = transponders
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entries.Count == 0)
        {
            return null;
        }

        var races = RacesIn(wanted, history, rounds);

        if (races.Count == 0)
        {
            return null;
        }

        var today = nowLocal ?? DateTime.Now;
        var tonight = TonightAtTheTrack.NightOf(today);

        string? biggestSince = null;
        var racesSince = 0;

        // RacesIn hands them over newest first, so the first race with a
        // field at least this big IS the most recent one, and everything
        // walked past on the way is a race that has run since.
        foreach (var race in races)
        {
            if (TonightAtTheTrack.NightOf(race.StartedAtUtc.ToLocalTime()) == tonight)
            {
                // Not compared with - see the remarks - but it has run
                // since, so it is counted.
                racesSince++;
                continue;
            }

            if (Starters(race) >= entries.Count)
            {
                biggestSince = When(race, today);
                break;
            }

            racesSince++;
        }

        var mains = rounds.All
            .Where(round => !string.IsNullOrWhiteSpace(round.Id) && round.Kind == RoundKind.Main)
            .Select(round => round.Id)
            .ToHashSet(StringComparer.Ordinal);

        var wonAMain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var race in races)
        {
            // The LOCAL year, like OnThisDay's date and for the same
            // reason: a main that finished at eight in the evening in New
            // York on 31 December is stamped the next year in UTC, and
            // "won a main this year" would quietly leave it out.
            if (race.EndedAtUtc.ToLocalTime().Year != today.Year
                || race.RoundId is not { } roundId
                || !mains.Contains(roundId))
            {
                continue;
            }

            foreach (var entry in race.Results)
            {
                if (entry.Position == 1 && !string.IsNullOrWhiteSpace(entry.Transponder))
                {
                    wonAMain.Add(entry.Transponder);
                }
            }
        }

        return new FieldStrength(
            wanted,
            entries.Count,
            races.Count,
            biggestSince,
            racesSince,
            entries.Count(t => wonAMain.Contains(t)));
    }

    /// <summary>
    /// What the night has done to the drivers on this grid, the biggest
    /// change first - the figure this app has worked out since track
    /// evolution was written and has never said about a person.
    ///
    /// THROUGH TonightAtTheTrack AND NOT ROUND IT. The per-driver change
    /// is what the class-wide evolution figure on the Next Up bar is the
    /// middle OF, so the two can never disagree about the same laps; a
    /// second implementation here would drift from the bar the first time
    /// a result was corrected mid-evening, and nothing would ever say so.
    /// See TonightAtTheTrack.Changes.
    ///
    /// Empty before anybody has had two races tonight, which is every
    /// club's first race of the night - and before a club has a programme
    /// at all, since a race with no round has no class.
    ///
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="transponders">Who is in the race about to run. Anyone else's night is not this intro's business.</param>
    /// <param name="className">The class to measure, or blank for every class racing tonight.</param>
    /// <param name="profiles">For the driver behind a transponder.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="rounds">Which round belongs to which class, and what each race is called.</param>
    /// <param name="nowLocal">Now, in local time, for which night counts as tonight. Defaults to now.</param>
    /// <param name="most">How many to offer. Two - see <see cref="MostNights"/>.</param>
    public static IReadOnlyList<DriverTonight> PaceTonight(
        IReadOnlyList<string> transponders,
        string? className,
        ProfileStore profiles,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        DateTime? nowLocal = null,
        int most = MostNights)
    {
        var racing = new HashSet<string>(
            transponders.Where(t => !string.IsNullOrWhiteSpace(t)),
            StringComparer.OrdinalIgnoreCase);

        if (racing.Count == 0)
        {
            return Array.Empty<DriverTonight>();
        }

        var found = new List<DriverTonight>();
        var want = Math.Max(1, most);

        foreach (var change in TonightAtTheTrack.PaceTonight(
                     history.All, id => rounds.Get(id), nowLocal ?? DateTime.Now, className))
        {
            if (!racing.Contains(change.Transponder))
            {
                continue;
            }

            var name = Typed(profiles.Get(change.Transponder)?.DriverName);

            // A driver with no profile has no name to say - the same answer
            // GridHistory gives, and for the same reason.
            if (name.Length == 0)
            {
                continue;
            }

            found.Add(new DriverTonight(
                change.Transponder,
                name,
                change.FirstRace,
                change.DeltaMs,
                change.Races));

            if (found.Count >= want)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// What the drivers on this grid have found since practice tonight,
    /// the biggest change first - "he was a second off this in practice".
    ///
    /// THE TIER 1 ITEM THE DEPTH PLAN ASKED FOR, built 11 Oct 2026. What
    /// had been built instead was race-against-race
    /// (<see cref="PaceTonight"/>), and the roadmap was explicit that this
    /// was not the same thing and that whoever picked it up had a decision
    /// to make first rather than a reader to write. The decision taken was
    /// to read practice through a helper of its OWN
    /// (TonightAtTheTrack.PracticeRuns) rather than letting it through
    /// TonightAtTheTrack.Races, so that nothing else in the app starts
    /// counting a practice run as a race - which is precisely what the
    /// roadmap warned a quiet change here would do.
    ///
    /// THROUGH TonightAtTheTrack AND NOT ROUND IT, like PaceTonight, so
    /// there is one implementation of a middle flying lap.
    ///
    /// Empty before anybody on this grid has both practised and raced
    /// tonight - and empty all evening at a club that does not run its
    /// practice through HeatCaller, which is the right answer for them.
    ///
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="transponders">Who is in the race. Anyone else's practice is not this segment's business.</param>
    /// <param name="className">The class whose RACES to measure. Practice is never scoped by it - see DriverAgainstPractice.</param>
    /// <param name="profiles">For the driver behind a transponder.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="rounds">Which round belongs to which class, and what each race is called.</param>
    /// <param name="nowLocal">Now, in local time, for which night counts as tonight. Defaults to now.</param>
    /// <param name="most">How many to offer. Two - see <see cref="MostNights"/>.</param>
    public static IReadOnlyList<DriverAgainstPractice> AgainstPractice(
        IReadOnlyList<string> transponders,
        string? className,
        ProfileStore profiles,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        DateTime? nowLocal = null,
        int most = MostNights)
    {
        var racing = new HashSet<string>(
            transponders.Where(t => !string.IsNullOrWhiteSpace(t)),
            StringComparer.OrdinalIgnoreCase);

        if (racing.Count == 0)
        {
            return Array.Empty<DriverAgainstPractice>();
        }

        var found = new List<DriverAgainstPractice>();
        var want = Math.Max(1, most);

        foreach (var change in TonightAtTheTrack.PracticeAgainstNow(
                     history.All, id => rounds.Get(id), nowLocal ?? DateTime.Now, className))
        {
            if (!racing.Contains(change.Transponder))
            {
                continue;
            }

            var name = Typed(profiles.Get(change.Transponder)?.DriverName);

            // A driver with no profile has no name to say - the same
            // answer PaceTonight and GridHistory give.
            if (name.Length == 0)
            {
                continue;
            }

            found.Add(new DriverAgainstPractice(
                change.Transponder,
                name,
                change.DeltaMs,
                change.Runs));

            if (found.Count >= want)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// What the club's garage and its own results know about the cars in a
    /// race just run - the car the main voice has never mentioned.
    ///
    /// IN FINISHING ORDER, so the winner's car is read first and is the one
    /// the recap carries if only one fits.
    ///
    /// WHY A DRIVER WITH TWO CARS IN THE CLASS GETS NOTHING
    ///
    /// The garage knows which cars are a driver's; it does not know which
    /// of them they put on the track. A driver with two stock cars and
    /// "the car they raced is the club's own" said about the wrong one is
    /// the invented fact this whole layer is written to prevent, so where
    /// there is more than one candidate the car is simply not described.
    /// Retired cars are left out first, which is what makes last season's
    /// chassis stop being a candidate.
    ///
    /// WHY THE CLASS COUNTS EXCLUDE THIS RACE
    ///
    /// Like the recap's driver history, this is read after the result has
    /// been FILED, so the race being recapped is already in the saved
    /// history. "Every one of their starts here before this one was in Box
    /// Stock" counted with this race in would count this race as one of
    /// them. See GridHistory's own remarks on the same promise.
    ///
    /// Call it on the UI thread: it reads the session history, the
    /// programme, the profiles and the garage.
    /// </summary>
    /// <param name="race">The race just filed.</param>
    /// <param name="className">The class it was run in, or blank for a race that is not a round - then there is no class to have moved into.</param>
    /// <param name="eventId">The race day it was run on, for a car lent for that day - see Car.DriverFor. Null when there is none.</param>
    /// <param name="profiles">For the driver behind a transponder.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="rounds">Which round belongs to which class.</param>
    /// <param name="cars">Every car in the garage.</param>
    /// <param name="nowLocal">Now, in local time, for how long a car has been in the garage. Defaults to now.</param>
    /// <param name="most">How many finishers to read. Four - see <see cref="MostCars"/>.</param>
    public static IReadOnlyList<RaceCar> Cars(
        SessionRecord race,
        string? className,
        string? eventId,
        ProfileStore profiles,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        IEnumerable<Car> cars,
        DateTime? nowLocal = null,
        int most = MostCars)
    {
        var finishers = race.Results
            .Where(entry => entry.Position > 0 && !string.IsNullOrWhiteSpace(entry.Transponder))
            .OrderBy(entry => entry.Position)
            .Take(Math.Max(1, most))
            .ToList();

        if (finishers.Count == 0)
        {
            return Array.Empty<RaceCar>();
        }

        var wanted = Typed(className);
        var today = nowLocal ?? DateTime.Now;

        // Everything the club has filed except the race being recapped -
        // see the remarks. Every class, because the question is which
        // classes this driver has raced.
        var before = RacesIn(null, history, rounds, race.Id);

        // Not ToDictionary: one duplicated round id in a hand-edited or
        // restored file would throw and cost the recap its cars, which is
        // the same care TrackLayoutStore takes with the same hazard.
        var classOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var round in rounds.All)
        {
            if (!string.IsNullOrWhiteSpace(round.Id))
            {
                classOf[round.Id] = Typed(round.ClassName);
            }
        }

        var garage = cars.Where(car => !car.Retired).ToList();
        var found = new List<RaceCar>();

        foreach (var entry in finishers)
        {
            var transponder = entry.Transponder;

            // The profile's name first and the saved one second, which is
            // what the mechanic's brief does with the same question: a
            // driver renamed since the race still gets their line, and a
            // driver with no profile at all still has the name the result
            // was filed under.
            var name = Typed(profiles.Get(transponder)?.DriverName);

            if (name.Length == 0)
            {
                name = Typed(entry.DriverName);
            }

            if (name.Length == 0)
            {
                continue;
            }

            var starts = 0;
            var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var anyWithoutClass = false;

            foreach (var past in before)
            {
                if (Placing(past, transponder) is null)
                {
                    continue;
                }

                starts++;

                var cls = past.RoundId is { } id && classOf.TryGetValue(id, out var named) ? named : string.Empty;

                if (cls.Length == 0)
                {
                    // A freeform race belongs to no class, so "every one of
                    // their starts here was in Box Stock" cannot be said
                    // about this driver at all. One is enough to drop it.
                    anyWithoutClass = true;
                }
                else
                {
                    classes.Add(cls);
                }
            }

            var onlyBefore = !anyWithoutClass && classes.Count == 1
                ? classes.First()
                : null;

            var firstInClass = wanted.Length > 0 && !classes.Contains(wanted)
                ? wanted
                : null;

            // Theirs for this race day: their own, or one lent to them for
            // it. Car.DriverFor answers both at once and will not let last
            // Tuesday's loan answer for tonight.
            var theirs = garage
                .Where(car => string.Equals(car.DriverFor(eventId), transponder, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Narrowed to the class when any of them are in it - the same
            // narrowing MechanicBrief does, and for the same reason: a
            // driver with a stock car and a mod car ran one of them.
            if (wanted.Length > 0)
            {
                var inClass = theirs
                    .Where(car => string.Equals(Typed(car.ClassName), wanted, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (inClass.Count > 0)
                {
                    theirs = inClass;
                }
            }

            var only = theirs.Count == 1 ? theirs[0] : null;
            var owned = Typed(only?.OwnerTransponder);

            // On loan when the car they are in for this race day is not
            // their own - a club car taken out for the night, or another
            // driver's lent to them.
            var onLoan = only is not null
                         && !string.Equals(owned, transponder, StringComparison.OrdinalIgnoreCase);

            found.Add(new RaceCar(
                transponder,
                name,
                ClubOwned: only?.IsClubCar == true,
                OnLoanForThisDay: onLoan,

                // Only for a car that is actually theirs, and only once
                // it has been theirs a while - see InGarageLeast. A car on
                // loan for the night was never "had". Compared in LOCAL
                // time, because today is local and Month converts to local
                // as well: mixing the two would put the bar an hour or two
                // out twice a year, which is not wrong enough for anybody
                // to notice and not right either.
                InGarageSince: only is not null && !onLoan && today - only.AddedUtc.ToLocalTime() >= InGarageLeast
                    ? Month(only.AddedUtc, today)
                    : null,
                FirstRaceInClass: firstInClass,
                OnlyClassBefore: onlyBefore,
                StartsBefore: starts));
        }

        return found;
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
    /// THE NIGHT'S THREAD, counted out of the club's own filed results -
    /// the truth that the one invented thing in this app is a framing OF.
    ///
    /// WHAT IT LOOKS FOR, AND WHY IT IS THIS AND NOT A RIVALRY
    ///
    /// One driver who has won the last race of the night at each of the
    /// club's last few race days. That, and nothing else: a night where
    /// four drivers won one thing each has no shape, and the right answer
    /// for it is null rather than a rivalry nobody in the room would
    /// recognise. There is deliberately no two-driver form of this.
    ///
    /// "THE LAST RACE OF THE NIGHT" IS MEANT LITERALLY, and that is a
    /// choice. OnThisDay picks a day's BIGGEST race and calls it the
    /// headline, which is a judgement - a club runs its A-main last and
    /// the biggest field is the race the night was about. That is the
    /// right rule for "what happened here in 2024". It is the wrong rule
    /// here, because this line is SPOKEN as a claim: a club that runs a
    /// B-main after the A would hear "the biggest race of the night" about
    /// a race it knows was not, and a claim a driver can disprove is the
    /// last one anybody believes. The last race of the night is a fact,
    /// so the line says that and this counts exactly it.
    ///
    /// TONIGHT IS NOT IN THE RUN. The run is counted over FINISHED race
    /// days only, which is what stops the thread drifting mid-evening: the
    /// figure is the same in the sixth heat as it was at the cold open, so
    /// the subject cannot change under the room's feet between the setup
    /// and the pay-off. What tonight has done to the run is reported
    /// separately - how many races the subject has been out in, how many
    /// they won, and WHO HAS ALREADY BEATEN THEM, which is the field the
    /// whole wrong-footing turns on.
    ///
    /// NOTHING IS REMEMBERED ANYWHERE. This is read fresh every time a
    /// segment is weighed up, so a thread the night has contradicted is
    /// not detected and corrected - it simply cannot be offered. See
    /// NightThread.
    ///
    /// THE SUBJECT HAS TO BE HERE. "Can anybody stop Dave tonight" asked
    /// about a driver who is not entered is not false, it is ridiculous,
    /// and a room notices that as fast as it notices a wrong fact. So the
    /// run only belongs to somebody on tonight's programme or already in
    /// one of tonight's results.
    ///
    /// AND IT HAS TO BE THE SAME PERSON. A transponder changes hands
    /// between seasons - TransponderTransfer exists for exactly that - so
    /// a run counted on the transponder alone can be a run belonging to
    /// two different drivers. Each win in the run is held to the name the
    /// RESULT saved against it matching the name the profile carries now,
    /// and the run stops at the first one that does not. A driver who has
    /// since changed their display name loses the thread, which is the
    /// right way round to fail.
    ///
    /// Demo and practice sessions count for nothing, the rule
    /// <see cref="RacesIn"/> states. A thread built on a demo race's
    /// invented finishing order is the worst version of this feature:
    /// every other reader honours it and this one has more riding on it
    /// than any of them.
    ///
    /// Null far more often than not - no programme, a club in its first
    /// month, four drivers, a night nobody has dominated, a subject who is
    /// not racing - and every one of those is the honest answer.
    ///
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="rounds">Tonight's programme, for who is racing and whether the night is over. Null at a club that has none.</param>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="profiles">For the driver behind a transponder, and the name the announcer calls them.</param>
    /// <param name="scope">The round ids belonging to the race day in hand, or null for the legacy holder where every round is tonight's - see <see cref="Upcoming"/>.</param>
    /// <param name="nowLocal">Now, in local time, for which night counts as tonight. Defaults to now.</param>
    public static NightThread? NightStory(
        RoundDefinitionStore? rounds,
        SessionHistoryStore history,
        ProfileStore profiles,
        IReadOnlySet<string>? scope = null,
        DateTime? nowLocal = null)
    {
        var today = nowLocal ?? DateTime.Now;
        var tonight = TonightAtTheTrack.NightOf(today);

        // Grouped by the race NIGHT and not the calendar date - six in the
        // morning to six in the morning, TonightAtTheTrack.NightOf's own
        // answer. A club night that runs past midnight is one night, and a
        // run that counted the mains as "another race day" would count the
        // same evening twice and read the number out over the PA.
        var byNight = new Dictionary<DateTime, List<SessionRecord>>();

        foreach (var record in history.All)
        {
            if (record.SessionType == RaceSessionType.Practice
                || record.IsDemoSession()
                || record.Results.Count == 0)
            {
                continue;
            }

            var night = TonightAtTheTrack.NightOf(record.StartedAtUtc.ToLocalTime());

            if (!byNight.TryGetValue(night, out var sessions))
            {
                sessions = new List<SessionRecord>();
                byNight[night] = sessions;
            }

            sessions.Add(record);
        }

        var earlier = byNight
            .Where(day => day.Key != tonight)
            .OrderByDescending(day => day.Key)
            .Select(day => day.Value)
            .ToList();

        // A club in its first month has no shape to be about - see
        // CommentaryShowFacts.LeastDaysOnFile. Judged here as well as in
        // the builder so a night that was never going to say anything does
        // not cost a walk of the history and the profiles.
        if (earlier.Count < CommentaryShowFacts.LeastDaysOnFile)
        {
            return null;
        }

        if (LastOfNight(earlier[0]) is not { } latest || WonBy(latest) is not { } champion)
        {
            return null;
        }

        var subject = Typed(champion.Transponder);

        // A ghost is a replay of an old lap put on the track for somebody
        // to chase (GhostService), not a driver with a run - the same
        // judgement Field makes about an entry.
        if (subject.Length == 0 || GhostService.IsGhost(subject))
        {
            return null;
        }

        var knownAs = Typed(profiles.Get(subject)?.DriverName);

        // No profile, no thread: without a name on file there is nothing to
        // hold the saved results to, so the run cannot be shown to belong
        // to one person. See the remarks on the transponder changing hands.
        if (knownAs.Length == 0)
        {
            return null;
        }

        var run = 0;
        var inTheRun = new List<SessionRecord>();

        foreach (var day in earlier)
        {
            if (LastOfNight(day) is not { } finale
                || WonBy(finale) is not { } winner
                || !string.Equals(Typed(winner.Transponder), subject, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Typed(winner.DriverName), knownAs, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            run++;
            inTheRun.AddRange(day);
        }

        if (run < CommentaryShowFacts.LeastDaysWon)
        {
            return null;
        }

        // Who the run was made against. Counted over every race on those
        // nights rather than only their last races, because "can anybody
        // stop that run" is a question about the club that was there - and
        // a four-driver club gets nothing, which is the bar
        // CommentaryShowFacts.LeastRivals sets.
        var rivals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var race in inTheRun)
        {
            foreach (var entry in race.Results)
            {
                var who = Typed(entry.Transponder);

                if (who.Length > 0
                    && !GhostService.IsGhost(who)
                    && !string.Equals(who, subject, StringComparison.OrdinalIgnoreCase))
                {
                    rivals.Add(who);
                }
            }
        }

        if (rivals.Count < CommentaryShowFacts.LeastRivals)
        {
            return null;
        }

        // Tonight's own races, oldest first - so the FIRST driver to beat
        // the subject is the first one found, which is the one the room
        // remembers.
        var races = byNight.TryGetValue(tonight, out var filed)
            ? filed.OrderBy(record => record.EndedAtUtc).ToList()
            : new List<SessionRecord>();

        // Tonight's programme, the same way NightProgress counts it: a null
        // scope is the legacy holder, where every round is tonight's.
        var programme = rounds is null
            ? new List<RoundDefinition>()
            : (scope is null ? rounds.All.ToList() : rounds.All.Where(round => scope.Contains(round.Id)).ToList());

        if (!Racing(programme, races).Contains(subject))
        {
            return null;
        }

        var started = 0;
        var won = 0;
        string? beatenBy = null;

        foreach (var race in races)
        {
            if (Placing(race, subject) is not { } position)
            {
                continue;
            }

            started++;

            if (position == 1)
            {
                won++;
                continue;
            }

            // Beaten, and only the first time it happened is recorded - the
            // thread is turned once a night however many races follow. An
            // empty name is a result saved without one and is still beaten:
            // null, and only null, means unbeaten. A race NOBODY won leaves
            // this alone, and the builder refuses the night rather than
            // calling them unbeaten in it.
            if (beatenBy is null && WonBy(race) is { } ahead)
            {
                beatenBy = Typed(ahead.DriverName);
            }
        }

        return new NightThread(
            subject,

            // The name the ANNOUNCER uses, not the profile's driver name -
            // the same SpokenName the voice queue and the result lines
            // read. A model handed two names for one driver writes about
            // two drivers, which in the segment the whole club listens to
            // is the one fault AsSpoken exists to prevent.
            Typed(profiles.Get(subject)?.SpokenName),
            run,
            earlier.Count,
            rivals.Count,
            started,
            won,

            beatenBy,

            // Measured the same way the run was - the last race of the
            // night - so extending the run is the same claim and not a
            // near-miss of it.
            races.Count > 0
                && LastOfNight(races) is { } tonightsLast
                && WonBy(tonightsLast) is { } tonightsWinner
                && string.Equals(Typed(tonightsWinner.Transponder), subject, StringComparison.OrdinalIgnoreCase),

            races.Count > 0 && programme.Count > 0 && programme.All(round => round.IsRecorded));
    }

    /// <summary>
    /// THE EVENING'S SHAPE, for the cold open: how many classes are out,
    /// how many drivers are in, and how many races there are to run.
    ///
    /// WHY THIS ONE READS THE PROGRAMME AND NOT ONE FILED RESULT
    ///
    /// It is said BEFORE A WHEEL HAS TURNED, and that is the whole of its
    /// character. Every other reader in this file counts something out of
    /// the club's own saved results; at the moment this one is asked there
    /// is nothing saved to count. So all of it comes off tonight's
    /// PROGRAMME, and not one field is gated on anything having been
    /// recorded - a reader that waited for a result would be silent at the
    /// one moment the cold open exists for, which is very nearly the fault
    /// the two bookends have had in the field all along (see
    /// MainForm.Commentary.cs, and the three segments fixed on 10 Oct
    /// 2026).
    ///
    /// "AFTER CHECK-IN" IS THE PROGRAMME, NOT THE CHECK-IN SHEET. The
    /// rosters were built from whoever was ticked in - RaceEvent's
    /// CheckedInDriversOnly, and ProgramStage - so the programme already IS
    /// the answer after check-in, and it is the only one that is actually
    /// about racing. A driver ticked in at the door and left out of every
    /// heat is not racing tonight, and a driver dropped into a heat by hand
    /// without being ticked in is, whatever the sheet says. Counting the
    /// sheet instead would read out a number nobody in the room could
    /// arrive at by looking at the run sheet on the wall.
    ///
    /// PRACTICE ROUNDS COUNT FOR NOTHING - the same judgement
    /// <see cref="RacesIn"/> makes about the history, applied to the
    /// programme. "Thirty races to run" at a club whose evening is ten
    /// heats and twenty practice slots is the app reading out its own
    /// booking sheet as a race meeting. A class only practising tonight is
    /// not racing either, and nor is a driver who is only in a practice
    /// slot.
    ///
    /// CLASSES AND DRIVERS ARE THE WHOLE EVENING'S; RACES ARE WHAT IS LEFT.
    /// The cold open is offered until it has actually been said, so it can
    /// land in the second gap of the night rather than the first - and "six
    /// classes and forty-one drivers" is as true then as it was at the
    /// door, where "thirty races to run" is not. So the two counts that
    /// describe the night describe all of it, and the one the builder words
    /// as "to run" is counted over the rounds that have not run. Neither
    /// can overstate what the room can see for itself.
    ///
    /// Never null, because it is a shape rather than an answer: a club with
    /// no programme comes back as nothing at all, and
    /// CommentaryShowFacts.ColdOpen refuses that on its own rather than
    /// opening the night with "Tonight: no drivers".
    ///
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="rounds">Tonight's programme. Null at a club that has none, which is a night with no shape to describe.</param>
    /// <param name="scope">The round ids belonging to the race day in hand, or null for the legacy holder where every round is tonight's - see <see cref="Upcoming"/>.</param>
    /// <param name="day">The race day in hand, for the venue the club typed on it. Null when no day is open, and then the opening is said without a place.</param>
    public static NightAhead NightAhead(
        RoundDefinitionStore? rounds,
        IReadOnlySet<string>? scope = null,
        RaceEvent? day = null)
    {
        // Tonight's rounds, counted the way NightStory and NightProgress
        // count them: a null scope is the legacy holder, where every round
        // is tonight's. Last week's programme is not tonight's shape, and
        // an opening built from it would announce a night the club is not
        // having.
        var programme = rounds is null
            ? new List<RoundDefinition>()
            : (scope is null ? rounds.All.ToList() : rounds.All.Where(round => scope.Contains(round.Id)).ToList());

        var racing = programme.Where(round => round.Kind != RoundKind.Practice).ToList();

        var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var drivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var round in racing)
        {
            var className = Typed(round.ClassName);

            // A round with no class on it is still a race to run - the
            // count below takes it - but it is not a class anybody can be
            // told about. Left out rather than counted as one more, which
            // is how a programme with three unnamed slots in it used to
            // become "four classes".
            if (className.Length > 0)
            {
                classes.Add(className);
            }

            foreach (var assigned in round.AssignedTransponders)
            {
                var who = Typed(assigned);

                // A ghost is a replay of an old lap put on the track for
                // somebody to chase (GhostService), not a driver who turned
                // up - the same judgement Field and NightStory make about an
                // entry. Three ghosts in the A-main must not become three
                // drivers in the room, least of all in the one line of the
                // night that is nothing but a count of the people in it.
                if (who.Length > 0 && !GhostService.IsGhost(who))
                {
                    drivers.Add(who);
                }
            }
        }

        var venue = Typed(day?.VenueName);

        return new NightAhead(
            classes.Count,
            drivers.Count,

            // What is still to run, not what the programme holds in total.
            // See the remarks: the cold open can land after the first heat,
            // and "thirty races to run" said with four of them already in
            // the history is the one number in the sentence the room can
            // immediately correct.
            racing.Count(round => !round.IsRecorded),

            // The club's own words for where it is racing, where it typed
            // them on the day. Empty is the common case - most clubs race
            // where they always race and have no reason to say so - and
            // then the builder opens with "Tonight:" instead.
            venue.Length > 0 ? venue : null,

            // TRAVELLED FROM: NULL, AND NOT BECAUSE IT WAS FORGOTTEN.
            //
            // The field asks how many drivers came from outside the club's
            // own membership, "where that is known" - and at this app it is
            // not known. There is no membership anywhere in HeatCaller: no
            // member list, no club identity of its own to be outside of,
            // and nothing on a profile that says guest. The nearest thing
            // is DriverProfile.HomeTrack, which is free text a club types
            // for intro colour at an open meeting, and the only way to turn
            // it into a count is to compare it with the day's venue name -
            // free text against free text. "Brighton MRC" against "Brighton
            // Indoor" makes a member a visitor; every regular who never
            // filled the box in makes themselves a local by accident.
            //
            // "Seven of them visiting" goes out over the PA as a fact, and
            // the people it is about are standing in the room able to count
            // themselves. A SHAPE may be invented in this layer; a NUMBER
            // never is - the rule NightThread states at length. So the cold
            // open says nothing about where anybody came from, and
            // CommentaryShowFacts.ColdOpen drops the clause it would have
            // gone in.
            //
            // ExcludeFromRecords is not it either: it means guests, staff
            // demo cars and anyone running outside the competition, all
            // under one tick, so counting it would announce the club's own
            // marshal's demo car as a visiting driver.
            null,

            // CHAMPIONSHIP NOTE: NULL, AND THIS IS THE FIELD TO LEAVE
            // ALONE.
            //
            // Its own doc comment says what it is - "the caller's words,
            // because only the season knows". It is a sentence a person
            // types, "two rounds left and four points covering the top
            // three", and there is nowhere in this app to type it: no note
            // on a Season, none on a championship, and RaceEvent.Notes is
            // the day's own scratch pad for weather and admin, which must
            // never be read out to a room.
            //
            // IT IS ALSO NOT FOR A READER TO WORK OUT. That sentence needs
            // the rounds remaining and the points still on offer, which is
            // precisely the one sum this file says out loud it does not do -
            // see "WHAT IT DELIBERATELY DOES NOT WORK OUT" at the top, and
            // Championship. A season line assembled here would also be a
            // second opinion against the ChampionshipCheck segment, which
            // has a reader and a builder of its own; two segments
            // disagreeing about who is leading in the same evening is worse
            // than one of them saying nothing.
            //
            // So: null, at both ends of the night. The builders drop the
            // clause, the opening and the sign-off are shorter, and nothing
            // is said that the club did not say first.
            null);
    }

    /// <summary>
    /// THE EVENING'S NUMBERS, for the sign-off: how many races ran, how
    /// many laps were turned, the quickest lap anybody did, the closest
    /// finish of the night, and how many drivers raced here for the first
    /// time.
    ///
    /// WHY THESE ARE WORTH READING OUT AT ALL
    ///
    /// Nobody in the room was counting. Everybody watched the racing and
    /// nobody knows that four hundred and twelve laps were turned, or that
    /// the closest finish all night was six hundredths - those are facts
    /// only the timing system holds, which is CommentaryShowFacts' own
    /// argument for the segment. It is also why every one of them is a
    /// number the club will be held to: each is counted out of the filed
    /// results or it comes back zero, and SignOff already drops what it has
    /// nothing for. A short sign-off beats a padded one.
    ///
    /// TONIGHT IS THE RACE NIGHT, SIX IN THE MORNING TO SIX IN THE MORNING
    /// - TonightAtTheTrack.NightOf, the same way NightStory groups the
    /// club's days. A club night that runs its mains past midnight is ONE
    /// night. Grouped by the calendar date the sign-off would hand the
    /// mains back as a second evening with four races and a hundred laps in
    /// it, and read the figure out to the people who had just watched the
    /// whole thing.
    ///
    /// NO ROUND SCOPE, deliberately, where NightStory takes one. A scope
    /// answers "which rounds are tonight's PROGRAMME", and none of this is
    /// about the programme: a race the club ran tonight counts whether it
    /// was filed against a round, against a round belonging to another day,
    /// or against no round at all. The sign-off is about what happened, not
    /// about what was planned.
    ///
    /// Demo and practice count for nothing, and a session with no result is
    /// not a race that ran - the rule <see cref="RacesIn"/> states. A
    /// rehearsal with "Save demo races to history" ticked was being counted
    /// into drivers' records until 10 Oct 2026; in here it would add its
    /// invented laps to the night's total and hand the fastest lap of the
    /// evening to the simulator.
    ///
    /// Never null. An evening nothing has been filed for comes back as no
    /// races and no laps, which CommentaryShowFacts.SignOff says nothing at
    /// all for - rather than signing off a night that has not started.
    ///
    /// Call it on the UI thread, like everything else here.
    /// </summary>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="profiles">For the driver behind a transponder, and the name the announcer calls them.</param>
    /// <param name="nowLocal">Now, in local time, for which night counts as tonight. Defaults to now.</param>
    public static NightDone NightDone(
        SessionHistoryStore history,
        ProfileStore profiles,
        DateTime? nowLocal = null)
    {
        var today = nowLocal ?? DateTime.Now;
        var tonight = TonightAtTheTrack.NightOf(today);

        var races = new List<SessionRecord>();

        // Every race night the club has filed BEFORE tonight, and everybody
        // who was in one - the two things the debut count needs. Gathered
        // in the same pass as tonight's races, because the alternative is
        // walking the whole history twice on the UI thread in the gap
        // between two heats. One walk is what NightStory already pays.
        var earlierNights = new HashSet<DateTime>();
        var racedBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in history.All)
        {
            if (record.SessionType == RaceSessionType.Practice
                || record.IsDemoSession()
                || record.Results.Count == 0)
            {
                continue;
            }

            // The night it STARTED on, like NightStory, so a race that went
            // out at ten to six in the morning belongs to the night the
            // club thinks it belongs to.
            var night = TonightAtTheTrack.NightOf(record.StartedAtUtc.ToLocalTime());

            if (night == tonight)
            {
                races.Add(record);
                continue;
            }

            earlierNights.Add(night);

            foreach (var entry in record.Results)
            {
                var who = Driver(profiles, entry.Transponder);

                if (who.Length > 0)
                {
                    racedBefore.Add(who);
                }
            }
        }

        if (races.Count == 0)
        {
            return new NightDone(0, 0);
        }

        var laps = 0;
        var fastestMs = 0L;
        string? fastestBy = null;
        var closestMs = long.MaxValue;
        string? closestBy = null;
        var tonightsDrivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var race in races)
        {
            foreach (var entry in race.Results)
            {
                var who = Typed(entry.Transponder);

                // A lap a ghost "turned" was turned on another night by
                // somebody else - the judgement Field, NightStory and
                // TonightAtTheTrack all make - so the night's total leaves
                // it out. A club running three ghosts in every heat would
                // otherwise sign off on half again as many laps as were
                // driven.
                if (who.Length == 0 || GhostService.IsGhost(who))
                {
                    continue;
                }

                // Negative is impossible today and costs nothing to refuse:
                // a lap penalty is applied as a total (SessionResultEntry's
                // LapPenalty), and one typed bigger than the laps driven
                // must not come off the night's count.
                laps += Math.Max(0, entry.Laps);

                var driver = Driver(profiles, who);

                if (driver.Length > 0)
                {
                    tonightsDrivers.Add(driver);
                }

                // THE FASTEST LAP OF THE NIGHT, and only the actual fastest
                // one. A disqualified result is not a lap the club is
                // reading anybody's name off, and nor is one from a driver
                // the club has deliberately kept out of its records - the
                // same two filters TonightAtTheTrack.FastestLaps applies to
                // the figure on the Next Up bar and the Big Screen, so the
                // sign-off and the club's own screens cannot disagree about
                // the same lap.
                //
                // NOTE WHAT IS NOT DONE HERE: if the quickest lap turns out
                // to belong to somebody with no name on file, the seconds
                // are still handed over with no holder and the builder
                // drops the whole line. Falling through to the quickest lap
                // that HAS a name would announce a slower lap as the
                // fastest of the night, which is a false claim rather than
                // a missing one.
                if (entry.FastLapMs > 0
                    && (fastestMs == 0 || entry.FastLapMs < fastestMs)
                    && ResultEditor.NormaliseOutcome(entry.Outcome) != ResultEditor.Dq
                    && profiles.Get(who) is not { ExcludeFromRecords: true })
                {
                    fastestMs = entry.FastLapMs;
                    fastestBy = SaidAs(profiles, who) is { Length: > 0 } quickest ? quickest : null;
                }
            }

            if (ClosestFinish(race, profiles) is { } finish && finish.Ms < closestMs)
            {
                closestMs = finish.Ms;
                closestBy = finish.Winner;
            }
        }

        return new NightDone(
            races.Count,
            laps,

            // Milliseconds to seconds and no further. Every time and margin
            // in this layer is WORDED by the builders through
            // CommentaryFacts.Invariant, and a reader that formatted one
            // itself is how a comma-decimal PC once put "6,935s" in front of
            // a model and had the PA read out "six, nine hundred thirty
            // five".
            fastestMs > 0 ? fastestMs / 1000.0 : 0,
            fastestBy,

            closestMs == long.MaxValue ? 0 : closestMs / 1000.0,
            closestBy,

            // DEBUTANTS: who raced here tonight with no race here before
            // it, as the club's own filed results have it.
            //
            // AND NOTHING AT ALL FROM A CLUB THAT HAS ONLY JUST INSTALLED
            // THIS. In its first month every driver's first filed race is
            // this week, so the line would be "nineteen drivers raced here
            // for the first time tonight" at a club that has been racing
            // for fifteen years - the app reading out the age of its own
            // database as club history. The same bar, for the same reason,
            // as CommentaryShowFacts.LeastDaysOnFile and the thread that
            // uses it.
            //
            // NOT DriverProfile.FirstSeenUtc, which is when somebody typed
            // the profile in. A member whose profile was created at the
            // sign-up desk this evening because the club had never needed
            // one is not a debutant, and a genuine newcomer who signed up
            // online last week is one.
            earlierNights.Count < CommentaryShowFacts.LeastDaysOnFile
                ? null
                : (int?)tonightsDrivers.Count(who => !racedBefore.Contains(who)),

            // THE CALLER'S WORDS, and this app has nowhere for them. The
            // whole of the argument is on NightAhead above; it applies
            // unchanged to the way out. Null, and the sign-off is one block
            // shorter for it.
            null);
    }

    /// <summary>
    /// The winning margin in one race, in milliseconds, with the winner's
    /// name as the announcer says it - or null where there is no margin
    /// that can honestly be put in seconds.
    ///
    /// Ghosts and disqualified results come out first and the places close
    /// up behind them, which is how AchievementEvaluator reads a saved
    /// result and for the reason it gives: a ghost that finished ahead of
    /// you put you second, and a margin measured to a replay of last
    /// Tuesday is a margin nobody raced.
    ///
    /// BOTH CARS HAVE TO HAVE COVERED THE SAME DISTANCE. RC racing is run
    /// to laps and then time, so elapsed times on different lap counts are
    /// not comparable at all - the rule AchievementEvaluator states beside
    /// this same subtraction. A winner a lap clear has no margin in
    /// seconds, and "the closest finish all night was 41.2 seconds" taken
    /// off one is a number about nothing.
    ///
    /// A margin of zero or less is refused rather than passed on. It means
    /// the saved places and the saved times disagree - a result corrected
    /// since, or two rows filed with the same total - and because the
    /// sign-off takes the SMALLEST margin of the night, letting a zero
    /// through would win and then be dropped by the builder, hiding a real
    /// six-hundredths finish somewhere else in the evening.
    /// </summary>
    private static (long Ms, string? Winner)? ClosestFinish(SessionRecord race, ProfileStore profiles)
    {
        var finishers = race.Results
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Transponder))
            .Where(entry => !GhostService.IsGhost(entry.Transponder))
            .Where(entry => ResultEditor.NormaliseOutcome(entry.Outcome) != ResultEditor.Dq)
            .OrderBy(entry => entry.Position)
            .ToList();

        if (finishers.Count < 2)
        {
            return null;
        }

        var won = finishers[0];
        var second = finishers[1];

        if (won.Laps <= 0 || second.Laps != won.Laps || won.ElapsedMs <= 0 || second.ElapsedMs <= 0)
        {
            return null;
        }

        var margin = second.ElapsedMs - won.ElapsedMs;

        if (margin <= 0)
        {
            return null;
        }

        var name = SaidAs(profiles, Typed(won.Transponder));

        return (margin, name.Length > 0 ? name : null);
    }

    /// <summary>
    /// Who a transponder belongs to, as one key: the driver's own
    /// transponder where the club's profiles know it, and the transponder
    /// itself where they do not.
    ///
    /// Through ProfileStore.Get, which answers for a driver's SECOND
    /// transponder as readily as their first - so a driver who raced here
    /// last month on their old one and tonight on a new one the club has
    /// added to their profile is one person in both answers. Without that
    /// they would be counted a debutant at their own club, which is the one
    /// thing the sign-off's debut line must not do.
    ///
    /// Empty for a ghost and for nothing at all, so neither can be a
    /// driver who raced tonight.
    /// </summary>
    private static string Driver(ProfileStore profiles, string? transponder)
    {
        var who = Typed(transponder);

        if (who.Length == 0 || GhostService.IsGhost(who))
        {
            return string.Empty;
        }

        var owner = Typed(profiles.Get(who)?.Transponder);

        return owner.Length > 0 ? owner : who;
    }

    /// <summary>
    /// A driver's name as the ANNOUNCER says it, or empty where there is no
    /// name to say - picked the way NightStory picks one, for its reason:
    /// the sign-off and the result lines must never name two different
    /// drivers.
    ///
    /// SpokenName falls back to the TRANSPONDER when a profile carries no
    /// name at all (see Models.cs), which is right for a race board and
    /// wrong for a sentence - "the fastest lap of the night went to 4C1A9"
    /// is the PA reading a hardware id out to a room. So a profile with no
    /// driver name on it has no sayable name, exactly as NightStory
    /// requires one before it will hold a run to a person, and the builder
    /// drops the line rather than this guessing at a word.
    ///
    /// The saved result's own DriverName is deliberately not a fallback. A
    /// transponder changes hands between seasons (TransponderTransfer), so
    /// the name on an old result and the name on the profile can be two
    /// people - and every other spoken line in the evening reads the
    /// profile's.
    /// </summary>
    private static string SaidAs(ProfileStore profiles, string transponder)
    {
        if (profiles.Get(transponder) is not { } profile || Typed(profile.DriverName).Length == 0)
        {
            return string.Empty;
        }

        return Typed(profile.SpokenName);
    }

    /// <summary>
    /// Who is out tonight: everybody the programme has assigned, and
    /// everybody already in one of tonight's results.
    ///
    /// The union of the two on purpose. The programme is the answer before
    /// a wheel turns, which is what the cold open needs; the results are
    /// the answer a programme edited mid-evening cannot take away, and a
    /// driver who has raced tonight is here whatever the round store says.
    /// </summary>
    private static HashSet<string> Racing(
        IReadOnlyList<RoundDefinition> programme,
        IReadOnlyList<SessionRecord> tonight)
    {
        var racing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var round in programme)
        {
            foreach (var assigned in round.AssignedTransponders)
            {
                var who = Typed(assigned);

                if (who.Length > 0 && !GhostService.IsGhost(who))
                {
                    racing.Add(who);
                }
            }
        }

        foreach (var race in tonight)
        {
            foreach (var entry in race.Results)
            {
                var who = Typed(entry.Transponder);

                if (who.Length > 0 && !GhostService.IsGhost(who))
                {
                    racing.Add(who);
                }
            }
        }

        return racing;
    }

    /// <summary>
    /// A race night's last race: the one that FINISHED last, with the
    /// biggest field breaking a tie.
    ///
    /// Ended rather than started, because that is what "the last race of
    /// the night" means to the people who were there. The tie-break is for
    /// the pair of sessions a club occasionally files with the same
    /// timestamp - without one the answer would depend on the order the
    /// history happened to load in, and the same night would produce two
    /// different runs on two different laptops.
    /// </summary>
    private static SessionRecord? LastOfNight(IReadOnlyList<SessionRecord> night) =>
        night
            .OrderByDescending(record => record.EndedAtUtc)
            .ThenByDescending(Starters)
            .FirstOrDefault();

    /// <summary>
    /// Who won a race, as its saved result has it - ghosts left out, for
    /// the reason <see cref="Field"/> gives.
    ///
    /// Null for a race nobody won, which is a real thing: every car
    /// retired, or a result corrected until no first place is left. The
    /// callers treat that as "no run broken and no run extended" rather
    /// than guessing at a winner.
    /// </summary>
    private static SessionResultEntry? WonBy(SessionRecord race)
    {
        foreach (var entry in race.Results)
        {
            if (entry.Position == 1
                && !string.IsNullOrWhiteSpace(entry.Transponder)
                && !GhostService.IsGhost(entry.Transponder))
            {
                return entry;
            }
        }

        return null;
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
    /// <summary>
    /// The month something happened in, in the words a commentator would
    /// use: "March" this year, "March 2024" in another.
    ///
    /// NOT <see cref="Since"/>, and the two are deliberately separate.
    /// Since answers "how long ago was that win" and says nothing at all
    /// when the answer is "recently", because a recap calling last month a
    /// drought is describing a good run as a bad one. This answers "when
    /// was this record set", where three weeks ago is a perfectly good
    /// answer and silence is not one.
    ///
    /// InvariantCulture for the month's NAME, where Since uses the PC's.
    /// The rest of the line around this is English and is read by an
    /// English voice, so a German PC's "Marz" dropped into it is a word
    /// the voice mispronounces in front of the room. The rule the whole
    /// commentary layer follows for numbers (CommentaryFacts.Invariant),
    /// applied to the one month name that goes into a spoken record.
    /// </summary>
    private static string Month(DateTime whenUtc, DateTime nowLocal)
    {
        var when = whenUtc.ToLocalTime();

        return when.Year == nowLocal.Year
            ? when.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture)
            : when.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The day a past race was run, as a commentator would date it: "3
    /// October", or "3 October 2025" in another year.
    ///
    /// The LOCAL date, like OnThisDay's and for the same reason: a club
    /// night that starts at eight in the evening in New York is stamped
    /// the next day in UTC, and a race the club remembers as the third
    /// would be read out as the fourth. InvariantCulture, like
    /// <see cref="Month"/>.
    /// </summary>
    private static string When(SessionRecord race, DateTime nowLocal)
    {
        var when = race.EndedAtUtc.ToLocalTime();

        return when.Year == nowLocal.Year
            ? when.ToString("d MMMM", System.Globalization.CultureInfo.InvariantCulture)
            : when.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// How many cars actually started a race, as its saved result counts
    /// them - ghosts left out, for the reason <see cref="Field"/> gives,
    /// and each transponder once however the result was corrected.
    /// </summary>
    private static int Starters(SessionRecord race) =>
        race.Results
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Transponder) && !GhostService.IsGhost(entry.Transponder))
            .Select(entry => entry.Transponder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

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
