namespace HeatCaller;

/// <summary>
/// Which cars a running order (the position report) names, in which order,
/// and the gap it gives each of them. Pure, so the order a report reads can
/// be tested without a voice. The words are AnnouncementService's - see
/// MaybeAnnouncePositions.
/// </summary>
/// <remarks>
/// WHY (1 Nov 2026): the report was worded the instant the leader crossed
/// and then waited its turn for the speaker - behind the leader's own lap
/// call, then while it was synthesized, then for the time it takes to read
/// three names. On a five or six second Mini-Z lap the cars behind cross
/// the line in that time: a car that had passed second place on track was
/// over the line and second on the screen while the voice still named the
/// car it had passed as second, and the time given for second place was a
/// lap old. The order is now read again just before it is spoken, and
/// worded again from the board as it stands if anything in it has moved.
/// </remarks>
internal static class PositionReportOrder
{
    /// <summary>One car in the running order, with the gap said for it.</summary>
    /// <param name="Standing">The car, as the board had it when the order was read.</param>
    /// <param name="Gap">
    /// What is said for it: laps down to the leader when it is down a lap,
    /// otherwise the time to the car directly in front. Nothing for the leader.
    /// </param>
    /// <param name="VsLeader">The gap back to the leader, for the laps-down wording.</param>
    internal readonly record struct Row(RaceStanding Standing, RaceGap Gap, RaceGap VsLeader);

    /// <summary>
    /// The same ranking the leaderboard uses - most laps, then the lowest
    /// elapsed time - leaving out any car <paramref name="leaveOut"/> says
    /// is not read (a pace ghost while ghosts are not announced).
    /// </summary>
    internal static List<RaceStanding> Rank(IEnumerable<RaceStanding> standings, Func<string, bool>? leaveOut = null) =>
        standings
            .Where(s => leaveOut is null || !leaveOut(s.Transponder))
            .OrderByDescending(s => s.Laps)
            .ThenBy(s => s.ElapsedMs)
            .ToList();

    /// <summary>
    /// The top <paramref name="depth"/> of an already ranked field, each
    /// with the gap the report says for it.
    /// </summary>
    /// <param name="crossingMs">Race time at which a car (by transponder) completed a given lap, or null when it is not known.</param>
    internal static List<Row> Read(IReadOnlyList<RaceStanding> ranked, int depth, Func<string, int, long?> crossingMs)
    {
        var named = ranked.Take(Math.Max(0, depth)).ToList();
        var rows = new List<Row>(named.Count);

        for (var i = 0; i < named.Count; i++)
        {
            if (i == 0)
            {
                // Nobody is in front of the leader, so there is no gap.
                rows.Add(new Row(named[0], new RaceGap(0, null), new RaceGap(0, null)));
                continue;
            }

            // Laps down is measured against the LEADER, at the line. Down a
            // lap, that IS the whole answer; on the lead lap, the gap to the
            // car directly ahead, measured when each crossed the same lap.
            var vsLeader = Between(named[i], named[0], crossingMs);
            var gap = vsLeader.LapsDown > 0 ? vsLeader : Between(named[i], named[i - 1], crossingMs);

            rows.Add(new Row(named[i], gap, vsLeader));
        }

        return rows;
    }

    /// <summary>
    /// Whether the board has moved since <paramref name="read"/> was worded:
    /// a different car named, the same cars in another order, or any of
    /// them over the line again since (its gap is then a lap old) or given
    /// a penalty. Any of those, and the words already written are no longer
    /// what the screen shows.
    /// </summary>
    internal static bool Moved(IReadOnlyList<Row> read, IReadOnlyList<Row> now)
    {
        if (read.Count != now.Count)
        {
            return true;
        }

        for (var i = 0; i < read.Count; i++)
        {
            var was = read[i].Standing;
            var nowCar = now[i].Standing;

            if (!string.Equals(was.Transponder, nowCar.Transponder, StringComparison.OrdinalIgnoreCase)
                || was.Laps != nowCar.Laps
                || was.ElapsedMs != nowCar.ElapsedMs)
            {
                return true;
            }
        }

        return false;
    }

    private static RaceGap Between(RaceStanding car, RaceStanding ahead, Func<string, int, long?> crossingMs) =>
        RaceGaps.Compute(car.Laps, car.ElapsedMs, ahead.Laps, ahead.ElapsedMs, lap => crossingMs(ahead.Transponder, lap));
}
