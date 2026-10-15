namespace HeatCaller;

/// <summary>
/// One driver's qualifying, as the report needs it.
/// </summary>
/// <param name="DriverId">However the caller identifies a driver.</param>
/// <param name="Name">As it should be said.</param>
/// <param name="Position">Where they qualified. One is pole.</param>
/// <param name="BestLapSeconds">Their quickest lap of qualifying, or zero when they never set one.</param>
/// <param name="SeasonAveragePosition">Where they usually qualify in this class, when there is enough season to say.</param>
/// <param name="MadeTheCut">Whether they are in the top main.</param>
public readonly record struct QualifyingLine(
    string DriverId,
    string Name,
    int Position,
    double BestLapSeconds,
    double? SeasonAveragePosition = null,
    bool MadeTheCut = true);

/// <summary>
/// Where a class stands partway through its heats - the halfway
/// standings, which had a tick box, a title and a budget and could never
/// say a word until 10 Oct 2026.
///
/// WHY THE ORDER IS THE QUALIFYING CALCULATOR'S AND NOT A COUNT OF WINS
///
/// "Where things stand partway through the heats" is a provisional
/// qualifying order, and the club chooses how that is worked out - best
/// single heat, a consecutive-lap window, points. A second implementation
/// that disagreed with the grid sheet by a place would be worse than no
/// segment, so the reader hands over the calculator's own answer and this
/// phrases it. Nothing in here ranks anybody.
/// </summary>
/// <param name="ClassName">Whose heats.</param>
/// <param name="RoundsRun">How many of the class's rounds tonight have been recorded.</param>
/// <param name="RoundsTotal">How many it has altogether tonight.</param>
/// <param name="Order">The provisional order as the club's own qualifying method has it, best first.</param>
public sealed record MeetingSoFar(
    string ClassName,
    int RoundsRun,
    int RoundsTotal,
    IReadOnlyList<QualifyingLine> Order);

/// <summary>
/// Where one driver stands in a championship, before and after tonight's
/// race.
///
/// Not DriverStats' ChampionshipLine, which is the same subject at a
/// different moment: that one is where a driver stands now, for their
/// profile card. This one is the pair of positions either side of a
/// race, because "takes over the lead" is the only thing in a
/// championship report that is actually news.
/// </summary>
/// <param name="DriverId">However the caller identifies a driver.</param>
/// <param name="Name">As it should be said.</param>
/// <param name="PositionNow">Where they stand after the race.</param>
/// <param name="PositionBefore">Where they stood before it.</param>
/// <param name="Points">Their total now, after drops.</param>
/// <param name="BehindLeader">Points off the lead, or zero for the leader.</param>
/// <param name="StillAlive">Whether the title is still mathematically theirs to win.</param>
public readonly record struct ChampionshipStanding(
    string DriverId,
    string Name,
    int PositionNow,
    int PositionBefore,
    int Points,
    int BehindLeader,
    bool StillAlive = true);

/// <summary>
/// The two segments that only make sense once a block of racing has
/// finished: what qualifying decided, and what the race did to the season.
///
/// Both are the same shape - a table in, the two or three things worth
/// saying out - and both are built on numbers the app has already worked
/// out. Nothing in here calculates a championship or a cut; the
/// QualifyingCalculator and the season's own scoring do that, and a second
/// implementation that disagreed with them by a point would be worse than
/// no segment at all. These choose and phrase.
/// </summary>
public static class CommentaryReportFacts
{
    /// <summary>How many places better than their season average before somebody is a mover.</summary>
    public const double MoverPlaces = 3;

    /// <summary>How close a miss has to be for missing the cut to be the story rather than the result.</summary>
    public const double NarrowMissSeconds = 0.5;

    /// <summary>A gap this far inside a second makes pole a statement rather than a result.</summary>
    public const double DominantPoleSeconds = 0.4;

    /// <summary>
    /// How many names the halfway standings read out.
    ///
    /// Three. The top of a provisional order is the news; a whole class
    /// read out in position order is the grid sheet, which is on the wall
    /// and on the Big Screen already. It is also as many near-identical
    /// rows of name-and-number as a local llama3.2:3b will keep attached
    /// to the right names - the limit Championship.MaxMoves exists for.
    /// </summary>
    public const int MidMeetingNames = 3;

    /// <summary>
    /// The fewest rounds a class must have run before "where things stand"
    /// stands anywhere.
    ///
    /// Two. After one heat the provisional order IS that heat's result,
    /// which the recap has just read out - so the segment would be the
    /// same information, in the same gap, in different words. That is the
    /// exact complaint this work answers.
    /// </summary>
    public const int MidMeetingLeast = 2;

    /// <summary>
    /// How many facts the halfway standings carry: how far through, the
    /// order, and the leader's quickest lap. Three is all it builds - the
    /// cap is here so a caller can ask for fewer without this file having
    /// to know why.
    /// </summary>
    public const int MidMeetingFacts = 3;

    /// <summary>
    /// What qualifying decided, for the gap before the mains.
    ///
    /// Three things at most, in the order a commentator would reach for
    /// them: who is on pole and by how much, who out-drove their own form,
    /// and who just missed out. The last is the one nobody else in the
    /// room will have worked out, and it is the one the driver it happened
    /// to will remember.
    /// </summary>
    /// <param name="order">Everyone, in qualifying order.</param>
    /// <param name="className">Whose qualifying this was.</param>
    /// <param name="raceKey">Which block of racing, so the same report is fresh for the next class.</param>
    public static IReadOnlyList<FactBlock> QualifyingReport(
        IReadOnlyList<QualifyingLine> order,
        string className,
        string raceKey)
    {
        var found = new List<FactBlock>();

        if (order.Count == 0)
        {
            return found;
        }

        var cls = CommentaryFacts.Clean(className);
        var ranked = order.OrderBy(q => q.Position).ToList();
        var pole = ranked[0];
        var poleName = CommentaryFacts.Clean(pole.Name);

        if (poleName.Length > 0)
        {
            var text = $"{poleName} takes pole in {cls}.";

            // The margin only when both actually set a lap - a pole taken
            // because nobody else got round is not a margin.
            if (ranked.Count > 1)
            {
                var second = ranked[1];
                var gap = second.BestLapSeconds - pole.BestLapSeconds;

                if (pole.BestLapSeconds > 0 && second.BestLapSeconds > 0 && gap > 0)
                {
                    var runnerUp = CommentaryFacts.Clean(second.Name);

                    // Every margin in this file goes in with a decimal
                    // POINT whatever the PC's region: on a comma-decimal
                    // laptop "0,21s" was read out over the PA as "nought,
                    // twenty one". See CommentaryFacts.Invariant.
                    text = gap > DominantPoleSeconds
                        ? $"{poleName} takes pole in {cls} by {CommentaryFacts.Invariant(gap, "0.00")}s - a long way, for this class."
                        : runnerUp.Length > 0
                            ? $"{poleName} takes pole in {cls} by {CommentaryFacts.Invariant(gap, "0.00")}s from {runnerUp}."
                            : $"{poleName} takes pole in {cls} by {CommentaryFacts.Invariant(gap, "0.00")}s.";
                }
            }

            found.Add(new FactBlock($"pole:{raceKey}", text));
        }

        // Out-qualifying their own form, which is the only honest way to
        // call somebody a surprise: it is measured against themselves
        // rather than against what anybody expected.
        var mover = ranked
            .Where(q => q.SeasonAveragePosition is { } average && average - q.Position >= MoverPlaces)
            .OrderByDescending(q => q.SeasonAveragePosition!.Value - q.Position)
            .FirstOrDefault();

        if (mover.DriverId is { Length: > 0 })
        {
            var moverName = CommentaryFacts.Clean(mover.Name);

            if (moverName.Length > 0)
            {
                var places = mover.SeasonAveragePosition!.Value - mover.Position;

                found.Add(new FactBlock($"mover:{raceKey}:{mover.DriverId}",
                    $"{moverName} qualified {CommentaryFacts.Ordinal(mover.Position)}, about {CommentaryFacts.Invariant(places, "0")} places better than they usually manage in {cls}.",
                    FactTheme.Driver(mover.DriverId)));
            }
        }

        // The near miss. Only when it was near: "missed the A-main by two
        // seconds" is a result, not a story.
        var lastIn = ranked.LastOrDefault(q => q.MadeTheCut);
        var firstOut = ranked.FirstOrDefault(q => !q.MadeTheCut);

        if (lastIn.DriverId is { Length: > 0 }
            && firstOut.DriverId is { Length: > 0 }
            && lastIn.BestLapSeconds > 0
            && firstOut.BestLapSeconds > 0)
        {
            var missedBy = firstOut.BestLapSeconds - lastIn.BestLapSeconds;
            var missedName = CommentaryFacts.Clean(firstOut.Name);

            if (missedBy > 0 && missedBy <= NarrowMissSeconds && missedName.Length > 0)
            {
                found.Add(new FactBlock($"missed:{raceKey}:{firstOut.DriverId}",
                    $"{missedName} missed the top main by {CommentaryFacts.Invariant(missedBy, "0.00")}s.",
                    FactTheme.Driver(firstOut.DriverId)));
            }
        }

        return found;
    }

    /// <summary>
    /// Where a class stands partway through its heats.
    ///
    /// Three facts at most, and the first two carry no theme because they
    /// ARE the segment - see FactBlock.Theme. The progress line goes first
    /// on purpose: without it "Sam heads the order" sounds like a result,
    /// and it is not one.
    ///
    /// Nothing is said when the heats are done. A finished block of
    /// qualifying is the qualifying report's subject, with its own segment,
    /// its own budget and a better line to say about it - and two segments
    /// reading out one order in one gap is the fault, not the fix.
    /// </summary>
    /// <param name="soFar">The class, its progress and the provisional order - see <see cref="MeetingSoFar"/>.</param>
    /// <param name="raceKey">Which point in the night, so the next class and the next check get their own.</param>
    /// <param name="most">How many facts at most.</param>
    public static IReadOnlyList<FactBlock> MidMeetingStandings(
        MeetingSoFar soFar,
        string raceKey,
        int most = MidMeetingFacts)
    {
        var found = new List<FactBlock>();

        if (soFar.Order.Count == 0
            || soFar.RoundsRun < MidMeetingLeast
            || soFar.RoundsTotal <= soFar.RoundsRun)
        {
            return found;
        }

        var cls = CommentaryFacts.Clean(soFar.ClassName);
        var ranked = soFar.Order.OrderBy(q => q.Position).ToList();

        // "Partway through", never "halfway": the spec's own title says
        // halfway because that is what a club ticks, and the LINE must not,
        // because three of six is halfway and two of seven is not. A number
        // said wrong is the one thing this layer cannot afford, and the
        // guard above has already made sure the figures are a real fraction.
        found.Add(new FactBlock(
            $"midmeeting:{raceKey}:progress",
            cls.Length > 0
                ? $"Partway through the {cls} heats: {soFar.RoundsRun} of {Plural.Of(soFar.RoundsTotal, "round")} run."
                : $"Partway through the heats: {soFar.RoundsRun} of {Plural.Of(soFar.RoundsTotal, "round")} run."));

        // The order as the club's own method has it, said as PROVISIONAL.
        // Every word of that matters: it is not a result, nobody has
        // qualified yet, and a line that sounded like a result would be
        // the timing system announcing something that has not happened.
        var names = new List<string>();

        foreach (var line in ranked.Take(Math.Max(1, MidMeetingNames)))
        {
            var who = CommentaryFacts.Clean(line.Name);

            if (who.Length > 0)
            {
                names.Add($"{CommentaryFacts.Ordinal(line.Position)} {who}");
            }
        }

        if (names.Count > 0)
        {
            found.Add(new FactBlock(
                $"midmeeting:{raceKey}:order",
                cls.Length > 0
                    ? $"Provisional {cls} order as it stands, on the club's own qualifying method: {string.Join(", ", names)}."
                    : $"Provisional order as it stands, on the club's own qualifying method: {string.Join(", ", names)}."));
        }

        // The leader's quickest lap of the heats - a number nobody in the
        // room has, which is the whole argument for this layer. Themed, so
        // a night that has just talked about them reaches for the order
        // line alone instead.
        //
        // Said as their best lap and NOT as the reason they are top: the
        // club may rank on total laps or on a lap window, and "quickest,
        // therefore ahead" would be this builder inferring a rule it was
        // not given.
        var leader = ranked[0];
        var leaderName = CommentaryFacts.Clean(leader.Name);

        if (leader.BestLapSeconds > 0 && leaderName.Length > 0)
        {
            found.Add(new FactBlock(
                $"midmeeting:{raceKey}:pace:{leader.DriverId}",
                // A decimal POINT whatever the PC's region - on a
                // comma-decimal laptop "11,482s" was read out over the PA
                // as "eleven, four hundred eighty two". See
                // CommentaryFacts.Invariant.
                $"{leaderName}'s quickest lap of the heats so far: {CommentaryFacts.Invariant(leader.BestLapSeconds, "0.000")}s.",
                FactTheme.Driver(leader.DriverId)));
        }

        return found.Count <= most ? found : found.Take(Math.Max(1, most)).ToList();
    }

    /// <summary>
    /// What the race did to the season, for after a class's last main.
    ///
    /// A change of leader is the headline and goes first. After that, how
    /// close it is, and whether it is still a contest at all - a title
    /// decided with rounds to go is news, and a title still open between
    /// four drivers is a reason to come back next month.
    /// </summary>
    /// <param name="table">The standings AFTER the race, in order.</param>
    /// <param name="className">Whose championship.</param>
    /// <param name="raceKey">Which race decided it, so the next class gets its own.</param>
    public static IReadOnlyList<FactBlock> ChampionshipCheck(
        IReadOnlyList<ChampionshipStanding> table,
        string className,
        string raceKey)
    {
        var found = new List<FactBlock>();

        if (table.Count == 0)
        {
            return found;
        }

        var cls = CommentaryFacts.Clean(className);
        var ranked = table.OrderBy(c => c.PositionNow).ToList();
        var leader = ranked[0];
        var leaderName = CommentaryFacts.Clean(leader.Name);

        if (leaderName.Length == 0)
        {
            return found;
        }

        // Level on points is level, whichever way the tie-break falls -
        // the rule ChampionshipLive.Headline keeps. "Takes over the lead
        // from Alex" said of two drivers on 40 points each is false.
        var level = ranked.TakeWhile(c => c.Points == leader.Points).ToList();
        var secondName = level.Count > 1 ? CommentaryFacts.Clean(level[1].Name) : string.Empty;

        if (level.Count > 1)
        {
            found.Add(new FactBlock($"champlead:{raceKey}",
                level.Count == 2 && secondName.Length > 0
                    ? $"{leaderName} and {secondName} are level at the top of the {cls} championship on {leader.Points} points."
                    : $"{level.Count} drivers are level at the top of the {cls} championship on {leader.Points} points."));
        }

        // Nobody stood anywhere before - the season's first scoring race.
        // Leading it is true; "still leading" or "taking over" is not.
        // A PositionBefore of zero is a driver new to the table tonight.
        else if (!ranked.Any(c => c.PositionBefore >= 1))
        {
            var second = ranked.Count > 1 ? ranked[1] : (ChampionshipStanding?)null;

            found.Add(new FactBlock($"champlead:{raceKey}",
                second is { } next && next.BehindLeader > 0
                    ? $"{leaderName} leads the {cls} championship on {leader.Points} points, {next.BehindLeader} clear of {CommentaryFacts.Clean(next.Name)}."
                    : $"{leaderName} leads the {cls} championship on {leader.Points} points."));
        }

        // A new name at the top is the only thing here that is genuinely
        // news rather than a status report. Not first before (zero, new to
        // the table tonight, counts) is a change of leader.
        else if (leader.PositionBefore != 1)
        {
            var displaced = ranked.FirstOrDefault(c => c.PositionBefore == 1);
            var displacedName = CommentaryFacts.Clean(displaced.Name);

            found.Add(new FactBlock($"champlead:{raceKey}",
                displacedName.Length > 0
                    ? $"{leaderName} takes over the lead of the {cls} championship from {displacedName}, on {leader.Points} points."
                    : $"{leaderName} takes over the lead of the {cls} championship, on {leader.Points} points."));
        }
        else
        {
            var second = ranked.Count > 1 ? ranked[1] : (ChampionshipStanding?)null;

            found.Add(new FactBlock($"champlead:{raceKey}",
                second is { } next && next.BehindLeader > 0
                    ? $"{leaderName} still leads the {cls} championship on {leader.Points} points, {next.BehindLeader} clear of {CommentaryFacts.Clean(next.Name)}."
                    : $"{leaderName} leads the {cls} championship on {leader.Points} points."));
        }

        // Whether it is still a contest. The caller works out who is
        // mathematically alive, because only the season's own rules know
        // how many rounds are left and how the drops fall.
        //
        // ONLY WHEN THE CALLER ACTUALLY WORKED IT OUT (14 Oct 2026).
        // StillAlive defaults to true, and no reader in the app can fill it
        // in yet (see CommentaryReaders.Championship), so "everyone alive"
        // is what "not known" looks like. Counted anyway, a table of one -
        // the season's first round, one driver scored - came out as one
        // alive, and the announcer said "that settles it: nobody can catch"
        // a driver after a single race. So the count is only trusted when
        // somebody on the table has been marked out of it, and "nobody can
        // catch" needs somebody else on the table to be the nobody.
        var alive = ranked.Any(c => !c.StillAlive) ? ranked.Count(c => c.StillAlive) : 0;

        if (alive == 1 && ranked.Count > 1)
        {
            found.Add(new FactBlock($"champdecided:{raceKey}",
                $"That settles it: nobody can catch {leaderName} in {cls} now."));
        }
        else if (alive is >= 2 and <= 4)
        {
            found.Add(new FactBlock($"champalive:{raceKey}",
                $"{alive} drivers can still win the {cls} championship."));
        }

        // The biggest climber of the night - a driver who went from
        // nowhere to in the hunt is the other thing worth a word.
        var climber = ranked
            .Where(c => c.PositionBefore > c.PositionNow && c.PositionBefore - c.PositionNow >= 2)
            .OrderByDescending(c => c.PositionBefore - c.PositionNow)
            .FirstOrDefault();

        if (climber.DriverId is { Length: > 0 }
            && !string.Equals(climber.DriverId, leader.DriverId, StringComparison.Ordinal))
        {
            var climberName = CommentaryFacts.Clean(climber.Name);

            if (climberName.Length > 0)
            {
                found.Add(new FactBlock($"champclimb:{raceKey}:{climber.DriverId}",
                    $"{climberName} is up from {CommentaryFacts.Ordinal(climber.PositionBefore)} to {CommentaryFacts.Ordinal(climber.PositionNow)} in {cls}.",
                    FactTheme.Driver(climber.DriverId)));
            }
        }

        return found;
    }
}
