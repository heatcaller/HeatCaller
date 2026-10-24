namespace HeatCaller;

/// <summary>
/// The sector sums that read a SAVED result rather than a live standing.
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer P6): Sectors moved into the timing
/// library (HeatCaller.Timing), and SessionResultEntry - the saved-result
/// model, with its corrections and as-raced copy - is not timing and stays
/// in the app. This one overload of Sectors.Bests is the only part of
/// Sectors that reads it, so it lives here, word for word, as a C# 14
/// static extension on Sectors: every caller still writes
/// Sectors.Bests(result) exactly as before, and the compiler picks this one
/// for a SessionResultEntry and the library's for a RacerStanding.
/// </remarks>
public static class SectorsOfSavedResults
{
    extension(Sectors)
    {
        /// <summary>
        /// A saved result's best time through each sector - from its laps with
        /// sectors (not the start lap), cut the way its last such lap is - or null.
        /// </summary>
        public static long[]? Bests(SessionResultEntry result)
        {
            var laps = Enumerable.Range(1, Math.Max(0, result.LapTimesMs.Count - 1))
                .Select(result.SectorsOf)
                .Where(sectors => sectors is not null)
                .Select(sectors => sectors!)
                .ToList();

            if (laps.Count == 0)
            {
                return null;
            }

            var count = laps[^1].Length;
            long[]? best = null;

            foreach (var sectors in laps.Where(s => s.Length == count))
            {
                best ??= sectors.ToArray();

                for (var i = 0; i < count; i++)
                {
                    best[i] = Math.Min(best[i], sectors[i]);
                }
            }

            return best;
        }
    }
}
