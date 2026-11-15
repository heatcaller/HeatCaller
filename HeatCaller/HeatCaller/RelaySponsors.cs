namespace HeatCaller;

/// <summary>
/// The club's sponsors on the relay's Live page, and what phones saw of them.
///
/// WHY (5 Nov 2026, Asher: "add sponsorships to the live tab at the botton.
/// then the report from the plan"): the sponsor report has only ever been
/// able to count the announcer's reads, and says so - the slides on the big
/// screen belong to nobody in particular. The relay's Live page is the one
/// screen where each sponsor has a card of their own, so it is the one
/// place a "seen" can be counted honestly against a sponsor: the page
/// counts a sponsor once it is on a phone's screen (once per phone per half
/// hour) and every tap through to their website, and the relay keeps the
/// counts by the hour for <see cref="SponsorReport"/>.
///
/// Only sponsors switched on for the night are published - a sponsor whose
/// season is over keeps their copy on the laptop and leaves the page.
/// </summary>
internal static class RelaySponsors
{
    /// <summary>A logo on the card is never shown bigger than this.</summary>
    internal static readonly Size LogoSize = new(360, 160);

    private static readonly string[] PictureKinds = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    /// <summary>
    /// The public document: {sponsors: [{id, name, tagline, url, picture}]}.
    /// <paramref name="pictureOf"/> turns a picture file name into a data
    /// URI, or null - see <see cref="Picture"/>.
    /// </summary>
    internal static string Document(IReadOnlyList<SponsorBooking>? bookings, Func<string, string?> pictureOf, Func<string, string?>? logoOf = null)
    {
        // THE LOGO FIRST (14 Nov 2026, stream overlay plan section 1): a
        // sponsor's own logo, kept as a transparent PNG, is what the card was
        // always trying to show. The picture for their read - a 16:9 slide
        // made for the Big Screen, shrunk to a JPEG - is the fallback for a
        // sponsor with no logo, as every sponsor was before logos existed.
        // Same field, so the relay's page needs no change.
        logoOf ??= SponsorLogo.CardDataUri;

        var shown = (bookings ?? Array.Empty<SponsorBooking>())
            .Where(one => one.Enabled && !string.IsNullOrWhiteSpace(one.Id) && !string.IsNullOrWhiteSpace(NameOnTheWeb(one)))
            .Select(one => new
            {
                id = one.Id,
                name = NameOnTheWeb(one),
                tagline = (one.Tagline ?? string.Empty).Trim(),
                url = WebsiteOf(one),
                picture = (string.IsNullOrWhiteSpace(one.LogoFileName) ? null : logoOf(one.LogoFileName))
                          ?? (string.IsNullOrWhiteSpace(one.PictureFileName) ? null : pictureOf(one.PictureFileName)),
            })
            .ToList();

        return System.Text.Json.JsonSerializer.Serialize(new { sponsors = shown });
    }

    /// <summary>
    /// Whether the store's sponsors may go to the relay at all.
    ///
    /// WHY (2026-11-05, Bug hunt 11: sponsors): a store loaded from a
    /// sponsors.json that could not be read is empty only because the file
    /// was held, not because the club has no sponsors. Publishing it replaced
    /// the relay's good snapshot with {"sponsors":[]} for the whole session,
    /// the same thing bug hunt 10 stopped for the badges page. Nothing is sent
    /// instead, so the relay keeps the last list it had.
    /// </summary>
    internal static bool Publishable(SponsorStore store) => !store.LoadedUnreadable;

    /// <summary>The name as it is written on the page: the web name where one was given, else the name the announcer says.</summary>
    internal static string NameOnTheWeb(SponsorBooking sponsor) =>
        (string.IsNullOrWhiteSpace(sponsor.WebName) ? sponsor.Name ?? string.Empty : sponsor.WebName).Trim();

    /// <summary>
    /// Their website, only as an absolute http(s) address - "hobbyhut.com"
    /// typed without the scheme is given one; anything else is no link.
    /// </summary>
    internal static string WebsiteOf(SponsorBooking sponsor)
    {
        var typed = (sponsor.Website ?? string.Empty).Trim();

        if (typed.Length == 0 || typed.Any(char.IsWhiteSpace))
        {
            return string.Empty;
        }

        if (!typed.Contains("://", StringComparison.Ordinal))
        {
            typed = "https://" + typed;
        }

        return Uri.TryCreate(typed, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
               && uri.Host.Contains('.', StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : string.Empty;
    }

    /// <summary>
    /// A sponsor's picture as a small JPEG data URI, or null for a video, a
    /// missing file or one that will not open. Cached by name and the file's
    /// time, so a logo is encoded once rather than on every pass.
    /// </summary>
    internal static string? Picture(SponsorStore store, string fileName, Dictionary<string, string?> cache)
    {
        if (!PictureKinds.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
        {
            return null;
        }

        var path = store.ResolvePath(fileName);

        if (path is null)
        {
            return null;
        }

        var key = $"{fileName}|{File.GetLastWriteTimeUtc(path).Ticks}";

        if (cache.TryGetValue(key, out var held))
        {
            return held;
        }

        if (cache.Count > 64)
        {
            cache.Clear();
        }

        string? uri;

        try
        {
            using var image = Image.FromFile(path);
            uri = RelayDocuments.JpegDataUri(image, LogoSize);
        }
        catch
        {
            uri = null;
        }

        cache[key] = uri;
        return uri;
    }

    /// <summary>
    /// The relay's hourly counts, {"2026-11-05T22": {id: [seen, taps]}},
    /// added up for one race night - 6am to 6am local, the night every
    /// other night-scoped reader uses (TonightAtTheTrack.NightOf).
    /// </summary>
    internal static IReadOnlyDictionary<string, (int Seen, int Taps)> ForTheNight(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, (int Seen, int Taps)>>? hours,
        DateTime nightLocal)
    {
        var totals = new Dictionary<string, (int Seen, int Taps)>(StringComparer.Ordinal);

        if (hours is null)
        {
            return totals;
        }

        var night = nightLocal.Date;

        foreach (var (hour, counts) in hours)
        {
            if (!DateTime.TryParseExact(hour + ":00:00Z", "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var utc))
            {
                continue;
            }

            if (TonightAtTheTrack.NightOf(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime()) != night)
            {
                continue;
            }

            foreach (var (id, pair) in counts)
            {
                var was = totals.TryGetValue(id, out var t) ? t : (0, 0);
                totals[id] = (was.Item1 + Math.Max(0, pair.Seen), was.Item2 + Math.Max(0, pair.Taps));
            }
        }

        return totals;
    }

    /// <summary>The relay's answer to sponsors/views, read; null when it is not one.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, (int Seen, int Taps)>>? ParseViews(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("hours", out var hours) || hours.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }

            var result = new Dictionary<string, IReadOnlyDictionary<string, (int Seen, int Taps)>>(StringComparer.Ordinal);

            foreach (var hour in hours.EnumerateObject())
            {
                if (hour.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    continue;
                }

                var slot = new Dictionary<string, (int Seen, int Taps)>(StringComparer.Ordinal);

                foreach (var one in hour.Value.EnumerateObject())
                {
                    if (one.Value.ValueKind == System.Text.Json.JsonValueKind.Array && one.Value.GetArrayLength() == 2
                        && one.Value[0].ValueKind == System.Text.Json.JsonValueKind.Number && one.Value[1].ValueKind == System.Text.Json.JsonValueKind.Number
                        && one.Value[0].TryGetInt32(out var seen) && one.Value[1].TryGetInt32(out var taps))
                    {
                        slot[one.Name] = (seen, taps);
                    }
                }

                result[hour.Name] = slot;
            }

            return result;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
