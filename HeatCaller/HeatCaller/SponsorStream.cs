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
    /// <param name="Name">The name as the web shows it (RelaySponsors.NameOnTheWeb).</param>
    /// <param name="SpokenName">The booking's own Name, the one a read on air is begun with - see <see cref="Up"/>.</param>
    internal sealed record Logo(string Id, string Name, string Url, int Weight, string? SpokenName = null);

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

        // WHY (15 Nov 2026, bug hunt 21): the read is begun with the
        // booking's spoken Name ("Smith and co"), the logo list carries the
        // web name ("Smith & Co.") where there is one, and matching only the
        // second left a rival's logo up - and counted - through Smith's read.
        // Either name is theirs.
        if (!string.IsNullOrWhiteSpace(onAirName)
            && logos.FirstOrDefault(l => string.Equals(l.Name.Trim(), onAirName.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals((l.SpokenName ?? string.Empty).Trim(), onAirName.Trim(), StringComparison.OrdinalIgnoreCase)) is { } onAir)
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

        string? saved = null;

        lock (Gate)
        {
            var night = TonightAtTheTrack.NightOf(nowUtc.ToLocalTime());

            if (night != _night)
            {
                _night = night;
                Seconds.Clear();
                Graphics.Clear();
                GraphicSeen.Clear();
                _lastShownUtc = default;
            }

            var gap = nowUtc - _lastShownUtc;
            _lastShownUtc = nowUtc;

            if (gap <= TimeSpan.Zero || gap > MaxGap)
            {
                return;
            }

            var had = Seconds.TryGetValue(sponsorId, out var before) ? before : 0;
            var total = had + gap.TotalSeconds;
            Seconds[sponsorId] = total;

            // WHY (23 Nov 2026, bug hunt 24): kept every fifteen seconds of a
            // sponsor's logo time rather than on every poll (a page asks about
            // once a second), so a restart loses at most a turn's worth.
            if ((long)(had / TurnSeconds) != (long)(total / TurnSeconds))
            {
                saved = SaveLocked();
            }
        }

        Tell(saved);
    }

    /// <summary>
    /// Told tonight's logo time and graphic counts, as <see cref="Save"/>
    /// writes them, for whatever keeps the night across a restart (23 Nov
    /// 2026, bug hunt 24). Run outside the lock, on whichever thread counted.
    /// </summary>
    internal static Action<string>? Changed { get; set; }

    private static void Tell(string? saved)
    {
        if (saved is null)
        {
            return;
        }

        try
        {
            Changed?.Invoke(saved);
        }
        catch
        {
            // A save that failed is not a reason to stop counting.
        }
    }

    /// <summary>
    /// How tonight's logo time and graphic counts are kept across a restart
    /// (23 Nov 2026, bug hunt 24). Keyed to the race night inside, so last
    /// week's time can never come back - see <see cref="Restore"/>.
    /// </summary>
    private sealed record SavedNight(string Night, Dictionary<string, double>? Seconds, Dictionary<string, int>? Graphics);

    private static string NightKey(DateTime night) => night.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Tonight's logo time and graphic counts, for whatever holds the night across a restart.</summary>
    internal static string Save()
    {
        lock (Gate)
        {
            return SaveLocked();
        }
    }

    private static string SaveLocked() => System.Text.Json.JsonSerializer.Serialize(new SavedNight(
        NightKey(_night),
        new Dictionary<string, double>(Seconds, StringComparer.Ordinal),
        new Dictionary<string, int>(Graphics, StringComparer.Ordinal)));

    /// <summary>
    /// WHY (23 Nov 2026, bug hunt 24): the logo time and the "presented by"
    /// graphic counts lived in memory only, so a club that reopened
    /// HeatCaller mid-evening sent a sponsor report with every read and
    /// mention for the night but only the logo time since the restart - or
    /// no stream line at all. This picks tonight's back up - only when it
    /// was saved on this same race night; anything else, or a save that will
    /// not read, is passed over. The larger of saved and counted is taken for
    /// each sponsor, so laying it back twice changes nothing.
    /// </summary>
    internal static void Restore(string? saved, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(saved))
        {
            return;
        }

        SavedNight? night;

        try
        {
            night = System.Text.Json.JsonSerializer.Deserialize<SavedNight>(saved);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }

        if (night is null)
        {
            return;
        }

        lock (Gate)
        {
            var tonight = TonightAtTheTrack.NightOf(nowUtc.ToLocalTime());

            if (tonight != _night)
            {
                _night = tonight;
                Seconds.Clear();
                Graphics.Clear();
                GraphicSeen.Clear();
                _lastShownUtc = default;
            }

            if (!string.Equals(night.Night, NightKey(_night), StringComparison.Ordinal))
            {
                return;
            }

            foreach (var (id, seconds) in night.Seconds ?? new Dictionary<string, double>())
            {
                if (string.IsNullOrWhiteSpace(id) || double.IsNaN(seconds) || seconds <= 0)
                {
                    continue;
                }

                Seconds[id] = Math.Max(Seconds.TryGetValue(id, out var had) ? had : 0, seconds);
            }

            foreach (var (id, count) in night.Graphics ?? new Dictionary<string, int>())
            {
                if (string.IsNullOrWhiteSpace(id) || count <= 0)
                {
                    continue;
                }

                Graphics[id] = Math.Max(Graphics.TryGetValue(id, out var had) ? had : 0, count);
            }
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

    private static readonly Dictionary<string, int> Graphics = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DateTime> GraphicSeen = new(StringComparer.Ordinal);

    /// <summary>
    /// A stream page put up a "presented by" graphic for this sponsor - the
    /// fastest lap, a battle, the grid, the result (14 Nov 2026, overlay plan
    /// section 5). Counted once however many sources say so: the same
    /// sponsor's same moment within ten seconds is the same graphic.
    /// </summary>
    internal static void GraphicShown(string? sponsorId, string moment, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(sponsorId) || string.IsNullOrWhiteSpace(moment))
        {
            return;
        }

        string saved;

        lock (Gate)
        {
            var night = TonightAtTheTrack.NightOf(nowUtc.ToLocalTime());

            if (night != _night)
            {
                _night = night;
                Seconds.Clear();
                Graphics.Clear();
                GraphicSeen.Clear();
                _lastShownUtc = default;
            }

            var key = sponsorId + "|" + moment;

            if (GraphicSeen.TryGetValue(key, out var seen) && nowUtc - seen < TimeSpan.FromSeconds(10))
            {
                GraphicSeen[key] = nowUtc;
                return;
            }

            GraphicSeen[key] = nowUtc;
            Graphics[sponsorId] = (Graphics.TryGetValue(sponsorId, out var had) ? had : 0) + 1;

            // WHY (23 Nov 2026, bug hunt 24): kept at once, as the mentions
            // are - the restart this is for is usually a force-closed window.
            saved = SaveLocked();
        }

        Tell(saved);
    }

    /// <summary>How many "presented by" graphics each sponsor had on the stream on this race night, by sponsor id.</summary>
    internal static IReadOnlyDictionary<string, int> GraphicsOn(DateTime nightLocal)
    {
        lock (Gate)
        {
            return nightLocal.Date == _night.Date
                ? new Dictionary<string, int>(Graphics, StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _night = default;
            Seconds.Clear();
            Graphics.Clear();
            GraphicSeen.Clear();
            _lastShownUtc = default;
        }

        Changed = null;
    }
}
