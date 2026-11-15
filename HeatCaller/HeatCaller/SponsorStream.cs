namespace HeatCaller;

/// <summary>
/// SPONSOR LOGOS ON THE STREAM (14 Nov 2026, stream overlay plan section 5).
///
/// Whose logo the stream's sponsor graphic shows right now, and how long
/// each logo has been on screen tonight, for the sponsor report.
///
/// THE LAPTOP DECIDES, NOT THE PAGE. Every OBS source asks the same question
/// and gets the same answer, so two sources never show two sponsors, and
/// the time can be counted honestly here: a page only says "showing" (the
/// ?sponsor=1 on its poll) while the graphic is actually up - it holds the
/// logo back in the last minute, on the final lap and during a battle.
///
/// TAKING TURNS BY SHARE. Each sponsor with a logo gets turns of
/// <see cref="TurnSeconds"/> in proportion to the share of the reads they
/// bought (SponsorBooking.Weight) - two gets twice the turns of one. While a
/// sponsor's read is going out (SponsorOnAir), it is their logo, so the
/// stream matches what is being said.
/// </summary>
internal static class SponsorStream
{
    /// <summary>How long one sponsor's turn lasts.</summary>
    internal const int TurnSeconds = 15;

    /// <summary>A poll further apart than this is a page that stopped looking, not two seconds of logo.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(2.5);

    /// <summary>One sponsor with a logo, as the stream needs them.</summary>
    internal sealed record Logo(string Id, string Name, string Url, int Weight);

    /// <summary>
    /// The logo up now, or null when no sponsor has one. Their read on air
    /// wins; otherwise turns by share, the same for everyone who asks in the
    /// same fifteen seconds.
    /// </summary>
    internal static Logo? Up(IReadOnlyList<Logo>? logos, DateTime nowUtc, string? onAirName)
    {
        if (logos is null || logos.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(onAirName)
            && logos.FirstOrDefault(l => string.Equals(l.Name.Trim(), onAirName.Trim(), StringComparison.OrdinalIgnoreCase)) is { } onAir)
        {
            return onAir;
        }

        var turns = new List<Logo>();

        foreach (var logo in logos)
        {
            for (var i = 0; i < Math.Clamp(logo.Weight, 1, 10); i++)
            {
                turns.Add(logo);
            }
        }

        // Spread a heavier sponsor's turns through the round rather than
        // back to back: every sponsor's first turn, then every second turn...
        var spread = turns
            .Select((logo, at) => (logo, rank: turns.Take(at).Count(t => ReferenceEquals(t, logo))))
            .OrderBy(t => t.rank)
            .Select(t => t.logo)
            .ToList();

        var slot = (long)Math.Floor((nowUtc - DateTime.UnixEpoch).TotalSeconds / TurnSeconds);
        return spread[(int)(slot % spread.Count)];
    }

    private static readonly object Gate = new();
    private static DateTime _night;
    private static readonly Dictionary<string, double> Seconds = new(StringComparer.Ordinal);
    private static DateTime _lastShownUtc;

    /// <summary>
    /// A page with the sponsor graphic up asked again: the logo up now has
    /// been on screen since the last ask, at most a couple of seconds' worth.
    /// Several sources asking at once count once - the gap since the last
    /// ask is what is added, from whichever source asked.
    /// </summary>
    internal static void Shown(string? sponsorId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(sponsorId))
        {
            return;
        }

        lock (Gate)
        {
            var night = TonightAtTheTrack.NightOf(nowUtc.ToLocalTime());

            if (night != _night)
            {
                _night = night;
                Seconds.Clear();
                _lastShownUtc = default;
            }

            var gap = nowUtc - _lastShownUtc;
            _lastShownUtc = nowUtc;

            if (gap <= TimeSpan.Zero || gap > MaxGap)
            {
                return;
            }

            Seconds[sponsorId] = (Seconds.TryGetValue(sponsorId, out var had) ? had : 0) + gap.TotalSeconds;
        }
    }

    /// <summary>Whole seconds each sponsor's logo was on the stream on this race night, by sponsor id.</summary>
    internal static IReadOnlyDictionary<string, int> SecondsOn(DateTime nightLocal)
    {
        lock (Gate)
        {
            return nightLocal.Date == _night.Date
                ? Seconds.ToDictionary(p => p.Key, p => (int)Math.Round(p.Value), StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _night = default;
            Seconds.Clear();
            _lastShownUtc = default;
        }
    }
}
