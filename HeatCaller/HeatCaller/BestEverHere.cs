namespace HeatCaller;

/// <summary>
/// A driver's quickest lap ever on a track layout, before tonight (10 Nov
/// 2026, voice speed plan, phase 4) - so a personal best that also beats it
/// is called as what it is: "Dee Ploy has never been faster here".
/// </summary>
/// <remarks>
/// WHY. A personal best in a race is a lap quicker than the driver's own
/// earlier laps tonight. The one that beats every lap they have ever done on
/// this layout is the lap they will talk about on the way home - and the
/// announcer called the two the same. The calls being quick (eleven_v4_turbo,
/// render ahead) is what makes a call about the driver themselves land while
/// they are still on the lap after it.
///
/// By transponder, as the track record's bar is (AchievementEvaluator's
/// BestEverOn): the saved races carry it on every row. A driver with no
/// earlier race on the layout has no best to beat - their first night there
/// is quickest by default, which is not news.
/// </remarks>
internal static class BestEverHere
{
    /// <summary>Their quickest lap on <paramref name="layout"/> in <paramref name="history"/>, or null for none.</summary>
    internal static long? PreviousBest(IEnumerable<SessionRecord> history, string layout, string transponder)
    {
        if (string.IsNullOrWhiteSpace(layout) || string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        long? best = null;

        foreach (var record in history)
        {
            if (!string.Equals(record.TrackLayoutName, layout, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var entry in record.Results)
            {
                if (entry.FastLapMs > 0
                    && string.Equals(entry.Transponder, transponder, StringComparison.OrdinalIgnoreCase)
                    && (best is null || entry.FastLapMs < best))
                {
                    best = entry.FastLapMs;
                }
            }
        }

        return best;
    }
}
