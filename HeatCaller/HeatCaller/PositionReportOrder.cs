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
    /// What is said for it: how far it is behind the LEADER - laps down when
    /// it is down a lap, otherwise the time from the leader crossing the
    /// lap to this car crossing the same lap. The board's "Gap to P1" is the
    /// same figure. Nothing for the leader.
    /// </param>
    /// <param name="VsLeader">The gap back to the leader (the same as Gap since 1 Nov 2026; kept for the laps-down wording).</param>
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

            // Measured to the LEADER, at the line - the same gap the board
            // shows in its "Gap to P1" column (1 Nov 2026). It used to be
            // the time to the car directly ahead for third place and on,
            // so the voice gave P3 a different figure from the one on the
            // screen and it was heard as the wrong time. Down a lap, the
            // laps down is the whole answer.
            var vsLeader = Between(named[i], named[0], crossingMs);

            rows.Add(new Row(named[i], vsLeader, vsLeader));
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

    /// <summary>
    /// How many of <paramref name="rows"/>, from the top, have a gap that
    /// is up to date for the leader's lap <paramref name="leaderLap"/>: the
    /// leader, then each car that has crossed that lap too, or that is a
    /// lap or more down (its laps down is already the answer, and it will
    /// not cross that lap for a while). Stops at the first car still on its
    /// way to the line - its gap would be a lap old, and nothing after it
    /// can be read without skipping a place.
    /// </summary>
    /// <remarks>
    /// WHY (1 Nov 2026): the report was read the instant the leader crossed,
    /// when every car behind was still on the previous lap, so each gap it
    /// gave was measured a lap earlier. It now waits until all the cars it
    /// names are over the line - this says when they are.
    /// </remarks>
    internal static int SettledCount(IReadOnlyList<Row> rows, int leaderLap)
    {
        var settled = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (i > 0 && row.Standing.Laps < leaderLap && row.VsLeader.LapsDown < 1)
            {
                break;
            }

            settled++;
        }

        return settled;
    }

    /// <summary>
    /// The longest a report waits for the cars it names to cross the line,
    /// in milliseconds, given the leader's last lap time (0 when it is not
    /// known). A car on the lead lap crosses within about one of its own
    /// laps of the leader; half as long again covers a slower car, and past
    /// that the car has stopped or gone off - the report is read without
    /// it rather than not at all.
    /// </summary>
    internal static long WaitLimitMs(long leaderLapMs) =>
        leaderLapMs > 0
            ? Math.Clamp(leaderLapMs * 3 / 2, MinWaitMs, MaxWaitMs)
            : DefaultWaitMs;

    /// <summary>
    /// The places a report can name before waiting for the last of them is
    /// worth a word next to the setting. Three is the default.
    /// </summary>
    internal const int DepthWithoutWarning = 3;

    /// <summary>
    /// The note shown under the "down to P" setting, and whether it is a
    /// warning. The report waits for the last car it names to cross the
    /// line (see SettledCount), so the deeper it reads, the later after the
    /// leader it is heard - with a big gap back to P8 that can be most of a
    /// lap. Past <see cref="DepthWithoutWarning"/> places that is said as a
    /// warning; up to it, as a plain note of when the report is read.
    /// </summary>
    internal static (string Text, bool Warn) DepthNote(int depth)
    {
        var place = "P" + depth.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return depth > DepthWithoutWarning
            ? ($"Waits for {place} to cross too, so it's heard well after the leader.", true)
            : ($"Read once {place} has crossed the line too.", false);
    }

    internal const long MinWaitMs = 3_000;
    internal const long MaxWaitMs = 60_000;
    internal const long DefaultWaitMs = 15_000;

    private static RaceGap Between(RaceStanding car, RaceStanding ahead, Func<string, int, long?> crossingMs) =>
        RaceGaps.Compute(car.Laps, car.ElapsedMs, ahead.Laps, ahead.ElapsedMs, lap => crossingMs(ahead.Transponder, lap));
}
