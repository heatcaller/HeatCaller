using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>What became of one ask to take drivers' podium videos down (22 Oct 2026, driver experience).</summary>
internal enum ForgetMediaOutcome
{
    /// <summary>The relay took it; <see cref="ForgetMediaResult.Forgotten"/> says how many videos went.</summary>
    Done,

    /// <summary>Nothing was sent: the relay is off, not set up, or being cleared. Nothing of theirs can be going up either.</summary>
    NotSent,

    /// <summary>The relay is from before forget-media and has to be redeployed. Already said in the log.</summary>
    RelayTooOld,

    /// <summary>It did not go - offline, refused, timed out. Already said in the log; worth asking again later.</summary>
    Failed,
}

/// <summary>The outcome, and how many videos the relay took down.</summary>
internal readonly record struct ForgetMediaResult(ForgetMediaOutcome Outcome, int Forgotten);

/// <summary>
/// Taking a driver's podium videos off the club's page at once, when the
/// desk turns on "no photo online" or "no video online" for them (22 Oct
/// 2026, driver experience, item 12).
/// </summary>
/// <remarks>
/// WHY NOT WAIT FOR THE SEVEN-DAY EXPIRY. A driver who asks for their face
/// to come off the internet means now, not next week. The relay keeps each
/// video under the badge id it was uploaded with, so the laptop names the
/// badges (RelayCheckIn.IdFor) and the relay drops every podium video
/// carrying one - never a transponder. Their audio clips stay: those are
/// private to their own badge, and these flags are about what the public
/// sees. Other drivers' videos that show this driver in the field can't be
/// edited, so they last until they expire (design risk 5).
///
/// NOT THROUGH THE RESULTS QUEUE. One small POST, sent when asked, on the
/// uploads' own client (VideoHttp) - the same route a podium video takes up,
/// so a "Delete everything" in progress (QuiesceAsync) stops this too.
/// </remarks>
public sealed partial class RelayPublisher
{
    /// <summary>The relay takes at most this many badge ids in one forget-media request (server.ts forgetDriverMedia).</summary>
    private const int ForgetMediaBatch = 100;

    /// <summary>
    /// Asks the relay to take down every podium video of these drivers, by
    /// badge id. Never throws; anything that went wrong is in the log and
    /// in the answer. Ids that are not badge ids are ignored.
    /// </summary>
    internal async Task<ForgetMediaResult> ForgetDriverMediaAsync(IReadOnlyCollection<string> badgeIds, CancellationToken cancel = default)
    {
        var ids = (badgeIds ?? Array.Empty<string>())
            .Where(id => DriverNight.IsADriverId(id))
            .Select(id => id.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return new ForgetMediaResult(ForgetMediaOutcome.Done, 0);
        }

        var target = _target;

        if (!_settings.RelayEnabled || !target.Configured || !IsSafeForToken(target.Root))
        {
            return new ForgetMediaResult(ForgetMediaOutcome.NotSent, 0);
        }

        lock (_gate)
        {
            if (_quiesced)
            {
                return new ForgetMediaResult(ForgetMediaOutcome.NotSent, 0);
            }
        }

        var club = Uri.EscapeDataString(target.Club);
        var forgotten = 0;

        try
        {
            for (var start = 0; start < ids.Count; start += ForgetMediaBatch)
            {
                var batch = ids.Skip(start).Take(ForgetMediaBatch).ToList();

                // A short ceiling of its own: VideoHttp's five minutes is for
                // a 30 MB upload, and this is a few hundred bytes.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/driver/forget-media");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
                request.Content = new StringContent(JsonSerializer.Serialize(new { ids = batch }), Encoding.UTF8, "application/json");

                using var response = await VideoHttp.SendAsync(request, timeout.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var said = await ErrorFrom(response, timeout.Token).ConfigureAwait(false);

                    // A relay from before this route answers "No such
                    // endpoint." - or, older still, nothing at all.
                    if ((int)response.StatusCode == 404 && !string.Equals(said, "No such club.", StringComparison.Ordinal))
                    {
                        Log?.Invoke("[privacy] The relay can't take podium videos down yet - redeploy it (fly deploy --ha=false). Until then they expire after seven days.");
                        return new ForgetMediaResult(ForgetMediaOutcome.RelayTooOld, forgotten);
                    }

                    Log?.Invoke($"[privacy] The relay did not take the podium videos down: HTTP {(int)response.StatusCode}{(said is null ? "" : " - " + said)}. It will be asked again.");
                    return new ForgetMediaResult(ForgetMediaOutcome.Failed, forgotten);
                }

                forgotten += await ForgottenIn(response, timeout.Token).ConfigureAwait(false);
            }

            return new ForgetMediaResult(ForgetMediaOutcome.Done, forgotten);
        }
        catch (OperationCanceledException)
        {
            Log?.Invoke("[privacy] The relay did not answer about taking podium videos down. It will be asked again.");
            return new ForgetMediaResult(ForgetMediaOutcome.Failed, forgotten);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[privacy] Could not reach the relay to take podium videos down: {ex.Message}. It will be asked again.");
            return new ForgetMediaResult(ForgetMediaOutcome.Failed, forgotten);
        }
    }

    /// <summary>The relay's count of videos taken down - its {"forgotten": n} - or 0.</summary>
    private static async Task<int> ForgottenIn(HttpResponseMessage response, CancellationToken cancel)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);

            return doc.RootElement.TryGetProperty("forgotten", out var count) && count.TryGetInt32(out var n) ? Math.Max(0, n) : 0;
        }
        catch
        {
            return 0;
        }
    }
}
