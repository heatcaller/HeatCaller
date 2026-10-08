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

                    text = gap > DominantPoleSeconds
                        ? $"{poleName} takes pole in {cls} by {gap:0.00}s - a long way, for this class."
                        : runnerUp.Length > 0
                            ? $"{poleName} takes pole in {cls} by {gap:0.00}s from {runnerUp}."
                            : $"{poleName} takes pole in {cls} by {gap:0.00}s.";
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
                    $"{moverName} qualified {CommentaryFacts.Ordinal(mover.Position)}, about {places:0} places better than they usually manage in {cls}."));
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
                    $"{missedName} missed the top main by {missedBy:0.00}s."));
            }
        }

        return found;
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

        // A new name at the top is the only thing here that is genuinely
        // news rather than a status report.
        if (leader.PositionBefore > 1)
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
        var alive = ranked.Count(c => c.StillAlive);

        if (alive == 1)
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
                    $"{climberName} is up from {CommentaryFacts.Ordinal(climber.PositionBefore)} to {CommentaryFacts.Ordinal(climber.PositionNow)} in {cls}."));
            }
        }

        return found;
    }
}
