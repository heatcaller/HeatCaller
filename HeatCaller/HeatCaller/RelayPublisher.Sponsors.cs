using System.Net.Http.Headers;

namespace HeatCaller;

/// <summary>
/// What phones saw of each sponsor on the relay's Live page (5 Nov 2026) -
/// read back for the sponsor report. See RelaySponsors.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>The counts change slowly and only matter when a report is made: every three minutes is plenty.</summary>
    private static readonly TimeSpan SponsorViewsPollEvery = TimeSpan.FromMinutes(3);

    private long _nextSponsorViewsTick;

    private volatile IReadOnlyDictionary<string, IReadOnlyDictionary<string, (int Seen, int Taps)>>? _sponsorViews;

    /// <summary>
    /// Whether the club has sponsors on its page at all. Set by MainForm;
    /// nothing is asked of the relay for a club that has none.
    /// </summary>
    internal Func<bool>? HasSponsorsOnline { get; set; }

    /// <summary>The relay's hourly counts as last read, or null before the first read.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, (int Seen, int Taps)>>? SponsorViews => _sponsorViews;

    /// <summary>
    /// Once per pass, from the worker loop. Quiet on failure: the report
    /// says the page's counts could not be read rather than this nagging in
    /// the console, and the last good counts are kept.
    /// </summary>
    private void CollectSponsorViewsPass(CancellationToken token)
    {
        if (Environment.TickCount64 < _nextSponsorViewsTick)
        {
            return;
        }

        _nextSponsorViewsTick = Environment.TickCount64 + (long)SponsorViewsPollEvery.TotalMilliseconds;

        bool wanted;

        try
        {
            wanted = HasSponsorsOnline?.Invoke() ?? false;
        }
        catch
        {
            wanted = false;
        }

        var target = _target;

        if (!wanted || !IsSafeForToken(target.Root))
        {
            return;
        }

        try
        {
            var club = Uri.EscapeDataString(target.Club);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/sponsors/views");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var text = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();

            if (RelaySponsors.ParseViews(text) is { } views)
            {
                _sponsorViews = views;
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
