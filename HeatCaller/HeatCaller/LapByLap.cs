namespace HeatCaller;

/// <summary>
/// Where each driver was running after each of their laps - the numbers
/// behind the relay's lap-by-lap position graph and the Pos column of a
/// driver's lap list.
///
/// WHY (28 Oct 2026, asked for by Asher, matching LiveRC): a club's results
/// had place, laps and best lap and nothing in between, so the race itself -
/// who passed whom and when - was gone the moment it was filed. LiveRC
/// shows it, and clubs compare.
///
/// WORKED OUT ON THE LAPTOP, ONCE. The relay pages only draw what they are
/// given, and the laptop is the one place that has every car's laps side by
/// side. So the answer is computed here, tested here, and sent with the
/// result rather than worked out again by every phone that opens it.
///
/// HOW. LapTimesMs[0] is the start-lap slot (green flag to the line), so a
/// driver's k-th crossing of the line comes LapTimesMs[0..k] after the green
/// flag. At that moment the drivers ahead of them are exactly the ones who
/// had already crossed the line for the k-th time: anybody with more laps
/// crossed it earlier, and anybody on the same lap who crossed it first is
/// ahead on the road. A dead heat on the clock goes to whoever finished
/// higher, the same tie-break the finishing order used.
///
/// Ghosts are recordings racing alongside, not drivers, so they neither get
/// positions nor take one from anybody.
/// </summary>
internal static class LapByLap
{
    /// <summary>
    /// Each row's position after each flying lap, in step with
    /// <c>LapTimesMs.Skip(1)</c> - an empty array for a row with no flying
    /// laps, or a ghost. Rows are taken in finishing order, as a session
    /// stores them.
    /// </summary>
    internal static IReadOnlyList<int[]> Positions(IReadOnlyList<SessionResultEntry> rows)
    {
        // When each row crossed the line for the k-th time, k = 0 being the
        // start-lap slot. Null for a row that does not race for places.
        var crossings = rows
            .Select(r => r is null || Ghosts.IsGhost(r.Transponder) ? null : CrossingsOf(r.LapTimesMs))
            .ToList();

        var answer = new List<int[]>(rows.Count);

        for (var me = 0; me < rows.Count; me++)
        {
            var mine = crossings[me];

            if (mine is null || mine.Length < 2)
            {
                answer.Add(Array.Empty<int>());
                continue;
            }

            var places = new int[mine.Length - 1];

            for (var k = 1; k < mine.Length; k++)
            {
                var ahead = 0;

                for (var other = 0; other < rows.Count; other++)
                {
                    if (other == me || crossings[other] is not { } theirs || theirs.Length <= k)
                    {
                        continue;
                    }

                    if (theirs[k] < mine[k] || (theirs[k] == mine[k] && other < me))
                    {
                        ahead++;
                    }
                }

                places[k - 1] = ahead + 1;
            }

            answer.Add(places);
        }

        return answer;
    }

    /// <summary>The running total of a row's lap times: when it crossed the line each time, from the green flag.</summary>
    private static long[] CrossingsOf(List<long>? lapTimesMs)
    {
        if (lapTimesMs is null || lapTimesMs.Count == 0)
        {
            return Array.Empty<long>();
        }

        var at = new long[lapTimesMs.Count];
        long sum = 0;

        for (var i = 0; i < lapTimesMs.Count; i++)
        {
            sum += Math.Max(0, lapTimesMs[i]);
            at[i] = sum;
        }

        return at;
    }
}
