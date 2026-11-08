namespace HeatCaller;

/// <summary>
/// Where one driver stands in a championship after a race, and where they
/// stood before it.
/// </summary>
public sealed class ChampionshipMove
{
    public required string DriverName { get; init; }

    /// <summary>Their place in the table now, 1-based.</summary>
    public required int Position { get; init; }

    /// <summary>
    /// Their place before this race counted. Zero when this was their
    /// first scoring round, which is a different thing from not having
    /// moved and is worth saying differently.
    /// </summary>
    public required int PreviousPosition { get; init; }

    public required int TotalPoints { get; init; }

    /// <summary>Points between them and the championship leader. Zero for the leader.</summary>
    public required int PointsBehindLeader { get; init; }

    /// <summary>Whether they were in the race that just finished.</summary>
    public required bool RacedThisTime { get; init; }

    // --- What is behind the total (7 Nov 2026) ---
    //
    // All four were worked out by SeasonStore.ComputeStandings and shown on
    // the Seasons page, and none reached the commentary - so "best five of
    // seven" changed every table the recap read out and the recap could
    // never say why a driver who raced had not moved.

    /// <summary>Rounds of theirs the best-rounds rule dropped. Zero when it is off or they have raced no more than it keeps.</summary>
    public int DroppedRounds { get; init; }

    /// <summary>Points those dropped rounds were worth - SeasonStandingEntry.RawPoints less TotalPoints.</summary>
    public int DroppedPoints { get; init; }

    /// <summary>Points carried in from before HeatCaller (Season.CarriedPoints), already inside TotalPoints.</summary>
    public int CarriedIn { get; init; }

    /// <summary>
    /// Races they were entered in and did not finish: no laps, or marked DNF
    /// or DQ in Edit Result (which keeps the laps and the points). Bug hunt
    /// 14, 7 Nov 2026: this said "completed no laps", which only the first is.
    /// </summary>
    public int Dnfs { get; init; }

    /// <summary>Places gained (positive) or lost (negative). Zero for a first appearance.</summary>
    public int PlacesGained => PreviousPosition == 0 ? 0 : PreviousPosition - Position;
}

/// <summary>What one finished race did to one championship table.</summary>
public sealed class ChampionshipSwing
{
    public required string SeasonName { get; init; }

    /// <summary>Drivers from the race just finished, best championship position first.</summary>
    public required IReadOnlyList<ChampionshipMove> Moves { get; init; }

    /// <summary>Whoever leads the championship now, whether or not they were in this race.</summary>
    public required ChampionshipMove Leader { get; init; }

    /// <summary>How many races the championship has counted, this one included.</summary>
    public required int RoundsCounted { get; init; }
}

/// <summary>
/// Reads a finished race back out of the championship it just changed.
///
/// Separate from SeasonStore, which knows how to turn finishing positions
/// into points and nothing else, and from EventSpine, which knows which
/// races count. This asks the two of them the same question twice - with
/// the race and without it - and subtracts.
///
/// It exists because the one moment a club is actually listening is the
/// end of a main, and the title race is the thing nobody on the rostrum
/// has time to work out by then.
/// </summary>
public static class Championship
{
    /// <summary>
    /// How many drivers from the race are worth putting in front of a
    /// commentary model.
    ///
    /// Enough to tell the story at the top of the table, few enough that
    /// the model is not picking four figures out of twelve near-identical
    /// lines - which is exactly how it starts attributing them to the
    /// wrong people. The same reasoning as the per-driver lap times the
    /// recap deliberately leaves out.
    /// </summary>
    private const int MaxMoves = 4;

    /// <summary>
    /// The championship this race counted towards and what it did to it,
    /// or null when the race counts towards none.
    ///
    /// Call this on the UI thread. It reads the history, programme and
    /// event stores, and those are not safe to enumerate while that thread
    /// is writing to them - which it does, immediately, as soon as a race
    /// is recorded.
    /// </summary>
    public static ChampionshipSwing? SwingFor(
        SessionRecord session,
        SeasonStore? seasons,
        RoundDefinitionStore? rounds,
        RaceEventStore? events,
        SessionHistoryStore history,
        ProfileStore? profiles = null)
    {
        // A demo race scores nothing (see SeasonStore.ComputeStandings), so
        // it can never have moved a championship.
        if (seasons is null || rounds is null || session.Results.Count == 0 || session.IsNotScored())
        {
            return null;
        }

        foreach (var season in seasons.All)
        {
            // Demo races taken out here as well as inside ComputeStandings,
            // so the "rounds counted" figure below does not include them.
            var counted = EventSpine.SessionsFor(season, events, rounds, history)
                .Where(s => !s.IsNotScored())
                .ToList();

            // Only a championship this race actually counts towards. A club
            // running three series at once gets the one it just scored in,
            // not all three.
            if (!counted.Any(s => string.Equals(s.Id, session.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            var after = SeasonStore.ComputeStandings(season, counted, profiles);

            if (after.Count == 0)
            {
                continue;
            }

            // The same table without this race. Recomputed rather than
            // subtracted, because "best 5 of 7" can move a driver's total
            // without this race being the round that dropped - there is no
            // arithmetic shortcut that survives the drop rule.
            var before = SeasonStore.ComputeStandings(
                season,
                counted.Where(s => !string.Equals(s.Id, session.Id, StringComparison.Ordinal)).ToList(),
                profiles);

            var leaderPoints = after[0].TotalPoints;

            ChampionshipMove MoveFor(int index, bool raced)
            {
                var entry = after[index];

                var was = before.FindIndex(e =>
                    string.Equals(e.DriverName, entry.DriverName, StringComparison.OrdinalIgnoreCase));

                return new ChampionshipMove
                {
                    DriverName = entry.DriverName,
                    Position = index + 1,
                    PreviousPosition = was < 0 ? 0 : was + 1,
                    TotalPoints = entry.TotalPoints,
                    PointsBehindLeader = leaderPoints - entry.TotalPoints,
                    RacedThisTime = raced,
                    DroppedRounds = entry.DroppedRounds,
                    DroppedPoints = Math.Max(0, entry.RawPoints - entry.TotalPoints),
                    CarriedIn = entry.CarriedIn,
                    Dnfs = entry.Dnfs,
                };
            }

            // Names exactly as the standings hold them: the driver's name
            // at the time each result was saved, falling back to the
            // transponder. That is the identity ComputeStandings keys on,
            // so matching on anything else would quietly find nobody.
            var racedHere = session.Results
                .Select(r => string.IsNullOrWhiteSpace(r.DriverName) ? r.Transponder : r.DriverName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var moves = new List<ChampionshipMove>();

            for (var i = 0; i < after.Count && moves.Count < MaxMoves; i++)
            {
                if (racedHere.Contains(after[i].DriverName))
                {
                    moves.Add(MoveFor(i, raced: true));
                }
            }

            // Nobody in this race is in the table - possible when the
            // season counts a class these drivers do not appear in under
            // the names it holds. Nothing worth saying, so try the next
            // championship rather than returning an empty one.
            if (moves.Count == 0)
            {
                continue;
            }

            return new ChampionshipSwing
            {
                SeasonName = season.Name,
                Moves = moves,
                Leader = MoveFor(0, racedHere.Contains(after[0].DriverName)),
                RoundsCounted = counted.Count,
            };
        }

        return null;
    }

    /// <summary>
    /// The season's planned length and the rounds held so far, for the
    /// public standings' "what it takes" card (7 Nov 2026, bug hunt 14).
    ///
    /// The rounds held are race DAYS, counted the way the commentary counts
    /// them (CommentaryReaders.RoundsCountedSoFar), not the most rounds any
    /// driver has raced. That figure had each driver's dropped rounds added
    /// a second time, and a night with two counting races was two rounds.
    ///
    /// When the races cannot be put on days - a session ticked in by hand
    /// with no round, or two counting races on one night - planned comes
    /// back 0, which keeps the page quiet. A held of 0 would not: the page
    /// would then say every round is still to go. The page also prices each
    /// remaining round as one race, so two races a night would under-price
    /// every rival anyway.
    /// </summary>
    internal static (int Planned, int Held) WhatItTakesLength(
        Season season,
        IReadOnlyList<SessionRecord> counted,
        RaceEventStore? events)
    {
        var scored = counted.Where(s => !s.IsNotScored()).ToList();

        if (scored.Count == 0)
        {
            return (season.PlannedRounds, 0);
        }

        return CommentaryReaders.RoundsCountedSoFar(scored, events, null) is { } days
            ? (season.PlannedRounds, days)
            : (0, 0);
    }
}
