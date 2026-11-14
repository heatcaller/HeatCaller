namespace HeatCaller;

/// <summary>
/// Who can still win a championship, for the Championship page's "Can they
/// still win?" card (13 Nov 2026, the rework Asher picked: mock-up C with D's
/// card in it).
/// </summary>
/// <param name="RoundsCounted">Rounds run so far - race days, as RoundsCountedSoFar counts them.</param>
/// <param name="RoundsLeft">Rounds still to run, from the season's planned length. Zero once it is over.</param>
/// <param name="Rows">Every driver in the table, in table order, with whether they can still win it.</param>
internal sealed record TitleRace(int RoundsCounted, int RoundsLeft, IReadOnlyList<(string Driver, int Points, bool CanWin)> Rows);

public static partial class CommentaryReaders
{
    /// <summary>
    /// The whole table's title race - each driver, and whether they can still
    /// win - or null with the reason it can't be worked out.
    ///
    /// THE ANNOUNCER'S PROOF, NOT A SECOND ONE. Each rival is tried with
    /// <see cref="CanStillCatch"/>, exactly as <see cref="TitleOnTheLine"/>
    /// tries them before a race: their best case (the place that pays most,
    /// in every round left) against the leader's worst (nothing), played out
    /// through the drop rule. So the page and the PA can never disagree about
    /// who is still in it.
    ///
    /// It refuses where that proof refuses - no planned length, races that
    /// don't come to one a race day, a scheme that scores nothing or takes
    /// points away, a best-paying place too far down, a table too long - and
    /// says why, for the card to show. It leaves out the announcer's own
    /// story limits (too few rounds in, more than three still in it): those
    /// decide what is worth saying over a PA, not what is true.
    ///
    /// With no rounds left the table is the answer: only a driver level with
    /// the leader is still in it, because level is not beaten.
    /// </summary>
    internal static (TitleRace? Race, string Why) TitleRaceFor(
        Season season,
        IReadOnlyList<SessionRecord> counted,
        RaceEventStore? events,
        ProfileStore? profiles)
    {
        if (season.PlannedRounds <= 0)
        {
            return (null, "Set how many rounds the season has (Points & Rules) to see who can still win.");
        }

        var standings = SeasonStore.ComputeStandings(season, counted, profiles);

        if (standings.Count < 2)
        {
            return (null, standings.Count == 0 ? "Nothing counted yet." : "Only one driver has scored so far.");
        }

        if (RoundsCountedSoFar(counted, events, null) is not { } roundsCounted)
        {
            return (null, "Some of its races aren't one per race day, so the rounds left can't be counted.");
        }

        var left = season.RoundsRemainingAfter(roundsCounted) ?? 0;
        var leader = standings[0];

        if (left == 0)
        {
            return (new TitleRace(roundsCounted, 0,
                standings.Select(s => (s.DriverName, s.TotalPoints, s.TotalPoints >= leader.TotalPoints)).ToList()), string.Empty);
        }

        var bestPaying = season.BestPayingPosition();

        if (bestPaying < 1)
        {
            return (null, "No place scores any points, so the rounds left can't change anything.");
        }

        if (season.PenalisesAnyone())
        {
            return (null, "A place takes points away, so the leader's worst round can't be worked out.");
        }

        if (bestPaying > MostFillersToProve + 1 || standings.Count > MostInTableToProve)
        {
            return (null, "The table is too big to work this out.");
        }

        var rows = new List<(string, int, bool)> { (leader.DriverName, leader.TotalPoints, true) };

        foreach (var rival in standings.Skip(1))
        {
            rows.Add((rival.DriverName, rival.TotalPoints,
                CanStillCatch(season, counted, left, bestPaying, leader.DriverName, rival.DriverName, profiles)));
        }

        return (new TitleRace(roundsCounted, left, rows), string.Empty);
    }
}
