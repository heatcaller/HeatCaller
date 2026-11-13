namespace HeatCaller;

/// <summary>
/// Skipping a driver from a race and scoring them DNS - did not start
/// (12 Nov 2026, setup plan step 8).
///
/// WHY: a broken car used to come out of its race silently - the phone's
/// approved "Skip me" took the driver off the grid and nothing else - and
/// the desk had no way to do it at all short of editing the heat. Silently
/// is the trouble: the result then simply doesn't have them, which reads as
/// "wasn't in this heat" rather than "was in it and didn't start", and the
/// points sheet can't tell the two apart. Now the round remembers who was
/// skipped (RoundDefinition.SkippedDns) and its saved result carries them
/// as DNS (SessionRecord.DidNotStart), with no result row - they never ran,
/// so nothing that reads laps, positions or records sees a thing. Points
/// qualifying already scores a round not run as "DNS"; this makes the
/// result say the same.
/// </summary>
public static class SkipDns
{
    /// <summary>What a skip changed, for its undo: the grid as it was, and whether they were already marked.</summary>
    public sealed record Undo(List<string> GridBefore, bool WasMarked);

    /// <summary>
    /// Takes <paramref name="transponder"/> off <paramref name="race"/>'s grid
    /// and marks them DNS for it. Null when they weren't on the grid. The
    /// caller saves the round.
    /// </summary>
    public static Undo? Skip(RoundDefinition race, string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder)
            || !race.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var before = race.AssignedTransponders.ToList();
        var wasMarked = IsSkipped(race, transponder);

        race.AssignedTransponders = before
            .Where(t => !string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!wasMarked)
        {
            race.SkippedDns ??= new List<string>();
            race.SkippedDns.Add(transponder);
        }

        return new Undo(before, wasMarked);
    }

    /// <summary>
    /// Puts a skip back: the driver in their old place on the grid and the DNS mark off - and, when
    /// the race has run since, the DNS off its saved result
    /// (<paramref name="result"/>, which the caller then saves).
    /// </summary>
    public static void PutBack(RoundDefinition race, string transponder, Undo undo, SessionRecord? result = null)
    {
        // 13 Nov 2026, Bug hunt 17: only this driver goes back, into the grid
        // as it is now. The whole grid from before the skip used to be put
        // back, which dropped anyone returned to the grid since (the Next Up
        // bar's "Put ... back") - off the grid with no DNS mark either.
        if (!race.IsRecorded && !race.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase))
        {
            var grid = race.AssignedTransponders.ToList();
            var was = undo.GridBefore.FindIndex(t => string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase));

            // Their old place: straight after the nearest driver who was
            // ahead of them and is still on the grid, or first with none.
            var at = was < 0 ? grid.Count : 0;

            for (var i = was - 1; i >= 0; i--)
            {
                var ahead = grid.FindIndex(t => string.Equals(t, undo.GridBefore[i], StringComparison.OrdinalIgnoreCase));

                if (ahead >= 0)
                {
                    at = ahead + 1;
                    break;
                }
            }

            grid.Insert(at, was < 0 ? transponder : undo.GridBefore[was]);
            race.AssignedTransponders = grid;
        }

        if (!undo.WasMarked)
        {
            race.SkippedDns?.RemoveAll(t => string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase));

            if (race.SkippedDns is { Count: 0 })
            {
                race.SkippedDns = null;
            }

            if (result?.DidNotStart is { } dns)
            {
                dns.RemoveAll(t => string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase));

                if (dns.Count == 0)
                {
                    result.DidNotStart = null;
                }
            }
        }
    }

    /// <summary>Whether the driver is marked DNS for this race.</summary>
    public static bool IsSkipped(RoundDefinition race, string transponder) =>
        race.SkippedDns?.Contains(transponder, StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Copies the round's DNS drivers onto its saved result as it is filed,
    /// leaving out anyone who raced after all (put back on the grid by hand,
    /// or heard on the loop) - a result row beats a DNS.
    /// </summary>
    public static void StampOnResult(SessionRecord record, RoundDefinition round)
    {
        if (round.SkippedDns is not { Count: > 0 } skipped)
        {
            return;
        }

        var raced = record.Results.Select(r => r.Transponder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dns = skipped.Where(t => !raced.Contains(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        record.DidNotStart = dns.Count == 0 ? null : dns;
    }

    /// <summary>"DNS: Max Power, Sam Price" for a result, or empty with nobody.</summary>
    public static string Words(SessionRecord record, Func<string, string> nameOf) =>
        record.DidNotStart is { Count: > 0 } dns
            ? "DNS (did not start): " + string.Join(", ", dns.Select(nameOf))
            : string.Empty;
}
