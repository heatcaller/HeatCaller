namespace HeatCaller;

/// <summary>
/// The club's pages elsewhere - its website, Facebook, Instagram and so on -
/// linked from the relay's pages: a row on the home page and a line at the
/// foot of every page.
///
/// Kept as a fixed list of platforms rather than free "label + address"
/// pairs, because the relay only accepts these keys and chooses the words
/// shown on the page itself (relay/src/site.ts, SOCIAL_LABELS). A key added
/// here has to be added there too, or the relay quietly drops it.
/// </summary>
internal static class RelaySocial
{
    /// <summary>
    /// One platform: the key the relay knows it by, what the app calls it,
    /// a hint for the empty box, and - where the platform has @handles - the
    /// address a bare handle becomes.
    /// </summary>
    internal sealed record Platform(string Key, string Label, string Placeholder, string? HandleUrl);

    /// <summary>In the order they appear, in the app and on the club's pages.</summary>
    internal static readonly IReadOnlyList<Platform> Platforms = new[]
    {
        new Platform("website", "Website", "https://www.yourclub.com", null),
        new Platform("facebook", "Facebook", "https://facebook.com/yourclub", "https://www.facebook.com/{0}"),
        new Platform("instagram", "Instagram", "@yourclub or https://instagram.com/yourclub", "https://www.instagram.com/{0}"),
        new Platform("youtube", "YouTube", "@yourclub or https://youtube.com/@yourclub", "https://www.youtube.com/@{0}"),
        new Platform("tiktok", "TikTok", "@yourclub or https://tiktok.com/@yourclub", "https://www.tiktok.com/@{0}"),
        new Platform("x", "X / Twitter", "@yourclub or https://x.com/yourclub", "https://x.com/{0}"),
        new Platform("discord", "Discord", "https://discord.gg/invite-code", null),
        new Platform("whatsapp", "WhatsApp", "https://chat.whatsapp.com/invite-code", null),
    };

    /// <summary>The same cap the relay applies; a longer address is dropped there.</summary>
    internal const int MaxUrlLength = 300;

    /// <summary>
    /// What was typed, as an address the relay will accept - or null with a
    /// reason. Empty is fine (no link) and comes back as an empty string.
    ///
    /// Forgiving about what people paste: "yourclub.com" gets https:// in
    /// front, and "@yourclub" on a platform with handles becomes the
    /// profile address. Strict about the rest, the same way the relay is:
    /// http or https only, no user name or password, no spaces.
    /// </summary>
    internal static string? Normalize(Platform platform, string typed, out string? problem)
    {
        problem = null;
        var text = (typed ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (text.StartsWith('@') && platform.HandleUrl is not null)
        {
            var handle = text.TrimStart('@');

            if (handle.Length == 0 || !handle.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            {
                problem = $"{platform.Label}: \"{text}\" doesn't look like a handle.";
                return null;
            }

            text = string.Format(platform.HandleUrl, handle);
        }
        else if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.UserInfo.Length > 0
            || !uri.Host.Contains('.'))
        {
            problem = $"{platform.Label}: that isn't a web address the club's pages can link to.";
            return null;
        }

        var url = uri.AbsoluteUri;

        if (url.Length > MaxUrlLength)
        {
            problem = $"{platform.Label}: that address is too long (over {MaxUrlLength} characters).";
            return null;
        }

        return url;
    }

    /// <summary>
    /// Each service's own hosts (and their subdomains), as the relay has them
    /// (relay/src/site.ts, SOCIAL_HOSTS). A website may be anywhere.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Hosts = new Dictionary<string, string[]>
    {
        ["facebook"] = new[] { "facebook.com", "fb.com", "fb.me" },
        ["instagram"] = new[] { "instagram.com", "instagr.am" },
        ["youtube"] = new[] { "youtube.com", "youtu.be" },
        ["tiktok"] = new[] { "tiktok.com" },
        ["x"] = new[] { "x.com", "twitter.com" },
        ["discord"] = new[] { "discord.gg", "discord.com", "discordapp.com" },
        ["whatsapp"] = new[] { "wa.me", "whatsapp.com" },
    };

    /// <summary>Whether an address (already normalised) is on its service's own host.</summary>
    internal static bool HostFits(Platform platform, string url)
    {
        if (!Hosts.TryGetValue(platform.Key, out var hosts))
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return hosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
    }

    /// <summary>
    /// What is wrong with the club's links as a set (19 Nov 2026, driver
    /// phone plan item 23 step 1): an address that isn't its service's own
    /// ("This doesn't look like an Instagram address"), or the same address
    /// typed for two services. Upstate Mini Z had one Facebook page in six
    /// boxes, so a driver tapping "Instagram" got Facebook. The relay drops
    /// both kinds; this says so on the laptop, where it can be fixed. Null
    /// when every link is fine. Typed text, by platform key.
    /// </summary>
    internal static string? SetProblem(IReadOnlyDictionary<string, string> typed)
    {
        var seen = new Dictionary<string, Platform>(StringComparer.OrdinalIgnoreCase);

        foreach (var platform in Platforms)
        {
            if (!typed.TryGetValue(platform.Key, out var text)
                || Normalize(platform, text, out _) is not { Length: > 0 } url)
            {
                continue;
            }

            if (!HostFits(platform, url))
            {
                return $"{platform.Label}: this doesn't look like {(platform.Key is "instagram" or "x" ? "an" : "a")} {platform.Label} address. It won't be shown.";
            }

            if (seen.TryGetValue(url, out var first))
            {
                return $"{platform.Label}: that's the same address as {first.Label}. Each link should go to its own page - it won't be shown twice.";
            }

            seen[url] = platform;
        }

        return null;
    }

    /// <summary>
    /// The links for the site document, in platform order, skipping empty
    /// and unusable ones - a bad entry that somehow reached the settings
    /// file is left off rather than sent for the relay to throw away. So is
    /// one on the wrong service's address, or one already sent for another
    /// service (item 23): the relay would drop it anyway.
    /// </summary>
    internal static List<object> ForSite(AppSettings settings)
    {
        var links = new List<object>();
        var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var platform in Platforms)
        {
            if (settings.RelaySocialLinks.TryGetValue(platform.Key, out var saved)
                && Normalize(platform, saved, out _) is { Length: > 0 } url
                && HostFits(platform, url)
                && sent.Add(url))
            {
                links.Add(new { kind = platform.Key, url });
            }
        }

        return links;
    }
}
