namespace HeatCaller;

/// <summary>
/// MOST IMPROVED TONIGHT (15 Nov 2026, Asher: "Make most improved"), for the
/// stream's between-races cards: the driver whose best lap came down the
/// most from their first race tonight to their quickest race since.
/// </summary>
/// <remarks>
/// <para>
/// Left out at first because SeasonPiece never says who improved, since it
/// ranks everyone else. Asher asked for it anyway. So it only ever names ONE
/// driver, with their own numbers, and never lists anybody below them.
/// Nobody is told they improved least.
/// </para>
/// <para>
/// BY THE PERCENTAGE, not the seconds: a tenth is a lot on a 9-second lap and
/// little on a 20-second one, and the classes run on the same night. Only on
/// the same track layout, because a different layout is a different lap.
/// Practice and not-scored races (demos, imports) don't count.
/// </para>
/// <para>
/// Only a real improvement: at least <see cref="MinGainMs"/> and
/// <see cref="MinPercent"/>%, so timing noise on a night when nobody really
/// got quicker never makes a winner.
/// </para>
/// </remarks>
public static class MostImprovedTonight
{
    /// <summary>The smallest gain that counts, in ms.</summary>
    internal const long MinGainMs = 100;

    /// <summary>The smallest gain that counts, as a percentage of their first best lap.</summary>
    internal const double MinPercent = 1.0;

    /// <summary>Who improved most, and by how much.</summary>
    /// <param name="Transponder">Their transponder.</param>
    /// <param name="FirstRace">The label of their first race tonight: "Junior - Heat 1".</param>
    /// <param name="FirstBestMs">Their best lap in that race.</param>
    /// <param name="BestSinceMs">Their best lap in any race of theirs since, on that layout.</param>
    public sealed record Pick(string Transponder, string FirstRace, long FirstBestMs, long BestSinceMs)
    {
        /// <summary>How much quicker, in ms.</summary>
        public long GainMs => FirstBestMs - BestSinceMs;

        /// <summary>How much quicker, as a percentage of the first.</summary>
        public double Percent => FirstBestMs <= 0 ? 0 : 100.0 * GainMs / FirstBestMs;

        /// <summary>"0.42 s quicker than in Junior - Heat 1", as it is READ on the stream.</summary>
        public string Line => (GainMs / 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            + " s quicker than in " + (string.IsNullOrWhiteSpace(FirstRace) ? "their first race" : FirstRace);
    }

    /// <summary>The most improved driver of <paramref name="tonight"/>, or null when nobody improved enough.</summary>
    public static Pick? From(IEnumerable<SessionRecord> tonight) => From(tonight, null);

    /// <summary>
    /// The most improved driver of <paramref name="tonight"/>, each race's
    /// class read from its round through <paramref name="roundOf"/> (or the
    /// class stamped on a shared heat's entry).
    /// </summary>
    /// <remarks>
    /// WHY (15 Nov 2026, bug hunt 21): keyed by transponder and layout only,
    /// a driver who runs Stock and then Mod on one transponder was "0.80 s
    /// quicker than in Stock" for changing to the faster car, and beat a
    /// driver who really found time. Now by class too: a lap is only ever
    /// compared with the same driver's laps in the same class.
    /// </remarks>
    public static Pick? From(IEnumerable<SessionRecord> tonight, Func<string, RoundDefinition?>? roundOf)
    {
        var races = (tonight ?? Array.Empty<SessionRecord>())
            .Where(r => r is not null && !r.IsNotScored() && r.SessionType != RaceSessionType.Practice)
            .OrderBy(r => r.StartedAtUtc)
            .ToList();

        // Per driver, layout and class: their first race's best, and their best since.
        var first = new Dictionary<(string, string, string), (string Label, long Ms)>();
        var since = new Dictionary<(string, string, string), long>();

        foreach (var race in races)
        {
            var roundClass = !string.IsNullOrEmpty(race.RoundId) && roundOf?.Invoke(race.RoundId) is { } round
                ? round.ClassName ?? string.Empty
                : string.Empty;

            foreach (var entry in race.Results ?? new List<SessionResultEntry>())
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Transponder) || entry.FastLapMs <= 0)
                {
                    continue;
                }

                // A shared heat stamps each entry with its own class; the
                // round's is only the main one there.
                var cls = string.IsNullOrWhiteSpace(entry.ClassName) ? roundClass : entry.ClassName;

                var key = (entry.Transponder.Trim().ToUpperInvariant(), (race.TrackLayoutName ?? string.Empty).Trim().ToUpperInvariant(),
                    (cls ?? string.Empty).Trim().ToUpperInvariant());

                if (!first.ContainsKey(key))
                {
                    first[key] = (race.Label ?? string.Empty, entry.FastLapMs);
                }
                else
                {
                    since[key] = since.TryGetValue(key, out var best) ? Math.Min(best, entry.FastLapMs) : entry.FastLapMs;
                }
            }
        }

        Pick? winner = null;

        foreach (var (key, later) in since)
        {
            var (label, firstMs) = first[key];
            var pick = new Pick(TransponderOf(races, key.Item1), label, firstMs, later);

            if (pick.GainMs < MinGainMs || pick.Percent < MinPercent)
            {
                continue;
            }

            if (winner is null
                || pick.Percent > winner.Percent
                || (pick.Percent == winner.Percent && pick.GainMs > winner.GainMs)
                || (pick.Percent == winner.Percent && pick.GainMs == winner.GainMs
                    && string.CompareOrdinal(pick.Transponder, winner.Transponder) < 0))
            {
                winner = pick;
            }
        }

        return winner;
    }

    /// <summary>The transponder as the race wrote it, for the key's upper-cased one.</summary>
    private static string TransponderOf(List<SessionRecord> races, string upper) =>
        races.SelectMany(r => r.Results ?? new List<SessionResultEntry>())
            .Select(e => e?.Transponder?.Trim() ?? string.Empty)
            .FirstOrDefault(t => t.ToUpperInvariant() == upper) ?? upper;
}
