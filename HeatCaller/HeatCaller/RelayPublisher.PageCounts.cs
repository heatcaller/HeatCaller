using System.Net.Http.Headers;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// HOW DRIVERS USE THE PAGES (19 Nov 2026, driver phone plan item 20 step 5).
///
/// The relay keeps a few anonymous counts per club per day - a phone losing
/// its signal and getting it back, Live losing touch with the track, the
/// install card, alerts opened, check-ins, pages opened (relay/src/
/// pagecounts.ts). Nothing showed them: they were read with the club's token
/// from GET /counts and nobody asked. This reads them every ten minutes while
/// publishing, and the Club Website page's Status card says what the last
/// seven days came to, so a fix can be judged by a race night. Counts only:
/// nothing in them says who.
/// </summary>
public sealed partial class RelayPublisher
{
    private static readonly TimeSpan PageCountsPollEvery = TimeSpan.FromMinutes(10);

    private long _nextPageCountsTick;

    /// <summary>The counts as last read, and the relay and club they were read from.</summary>
    private sealed record ReadCounts(RelayTarget From, RelayPageCounts.Totals Totals);

    private volatile ReadCounts? _pageCounts;

    /// <summary>
    /// The relay's last 7 and 30 days' counts as last read, or null before
    /// the first read - and null once the relay, club or token changes
    /// (bug hunt 22): the old club's counts were shown for the new one, and
    /// kept for good when the new relay refused or had no /counts.
    /// </summary>
    internal RelayPageCounts.Totals? PageCounts =>
        _pageCounts is { } read && read.From == _target ? read.Totals : null;

    /// <summary>Once per pass, from the worker loop, on its own tick. Quiet on failure; the last good counts are kept.</summary>
    private void CollectPageCountsPass(CancellationToken token)
    {
        if (Environment.TickCount64 < _nextPageCountsTick)
        {
            return;
        }

        _nextPageCountsTick = Environment.TickCount64 + (long)PageCountsPollEvery.TotalMilliseconds;
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        try
        {
            var club = Uri.EscapeDataString(target.Club);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/counts");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            if (RelayPageCounts.Parse(response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult()) is { } totals)
            {
                _pageCounts = new ReadCounts(target, totals);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Kept as it was; tried again on the next tick.
        }
    }
}

/// <summary>The relay's page counts (GET /counts), read and put into words. No window, no network: tested on its own.</summary>
internal static class RelayPageCounts
{
    /// <summary>The totals over the last 7 and 30 days, event name to count.</summary>
    internal sealed record Totals(IReadOnlyDictionary<string, int> Last7, IReadOnlyDictionary<string, int> Last30);

    /// <summary>The relay's answer, read; null when it isn't one.</summary>
    internal static Totals? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("last7", out var last7)
                || !root.TryGetProperty("last30", out var last30))
            {
                return null;
            }

            return new Totals(Read(last7), Read(last30));
        }
        catch (JsonException)
        {
            return null;
        }

        static Dictionary<string, int> Read(JsonElement element)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            if (element.ValueKind != JsonValueKind.Object)
            {
                return counts;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var n) && n > 0
                    && property.Name.Length <= 64)
                {
                    counts[property.Name] = n;
                }
            }

            return counts;
        }
    }

    /// <summary>
    /// One line for the Status card: what drivers' phones did over the last
    /// seven days, the parts that happened, in plain words. Null before the
    /// counts are read.
    /// </summary>
    internal static string? Describe(Totals? totals)
    {
        if (totals is null)
        {
            return null;
        }

        var week = totals.Last7;
        int Count(string name) => week.TryGetValue(name, out var n) ? n : 0;
        int Sum(string prefix) => week.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Sum(pair => pair.Value);

        var parts = new List<string>();

        var views = Sum("view_");
        if (views > 0) parts.Add(Plural.Of(views, "page opened", "pages opened"));

        var posters = Count("poster_opened");
        if (posters > 0) parts.Add($"{Plural.Of(posters, "visit")} from the poster");

        var offline = Count("offline_shown");
        if (offline > 0) parts.Add($"{Plural.Of(offline, "time")} a phone lost signal ({Count("offline_recovered")} back)");

        var stale = Count("stale_live_shown");
        if (stale > 0) parts.Add($"Live lost touch with the track {Plural.Of(stale, "time")}");

        var shown = Count("install_card_shown");
        if (shown > 0) parts.Add($"{Plural.Of(Count("install_accepted"), "install")} from {Plural.Of(shown, "card")} shown");

        var alerts = Sum("alert_opened_");
        if (alerts > 0) parts.Add(Plural.Of(alerts, "alert opened", "alerts opened"));

        var checkins = Count("checkin_tapped");
        if (checkins > 0) parts.Add(Plural.Of(checkins, "check-in"));

        var inApp = Sum("inapp_card_shown_");
        if (inApp > 0) parts.Add($"{Plural.Of(inApp, "visit")} from inside Facebook or Instagram");

        var late = Count("listen_late_dropped");
        if (late > 0) parts.Add($"{Plural.Of(late, "late call")} not played on Listen");

        return parts.Count == 0
            ? "Drivers' phones, last 7 days: nothing counted yet."
            : "Drivers' phones, last 7 days: " + string.Join(", ", parts) + ". Counts only - nothing says who.";
    }
}
