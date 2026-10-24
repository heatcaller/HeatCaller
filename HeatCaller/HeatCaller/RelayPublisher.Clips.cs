using System.Net.Http.Headers;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// What became of one driver's clip on its way to the relay (22 Oct 2026,
/// driver experience).
/// </summary>
/// <param name="Clip">The clip's address on the relay ("/c/club/clip/id.mp3") once it landed, otherwise null.</param>
/// <param name="Problem">Why it did not land, in the club's words, or null when it did (or was simply not sent).</param>
/// <param name="GiveUp">True when the rest of this race's clips would fail the same way - the relay is not set up for clips, or nights are off - so the caller stops trying.</param>
internal readonly record struct ClipUploadResult(string? Clip, string? Problem, bool GiveUp);

/// <summary>
/// Drivers' own race clips, on their way to the relay (22 Oct 2026, driver
/// experience).
///
/// After a race the recorder cuts each driver an mp3 of the moments they
/// were called (RaceAudioRecorder.WriteClips). When the club ticks "share
/// drivers' clips" and offers drivers their nights, each one goes up here,
/// keyed to that driver's BADGE ID and listed only on their own night - the
/// page only their badge code opens. Never a transponder, never a name: the
/// relay is told whose clip it is and nothing else about them.
///
/// THE PODIUM VIDEO'S ROUTE, COPIED (RelayPublisher.Videos.cs): straight up
/// on the recorder's background thread, not through the results queue, on
/// the uploads' own client (VideoHttp), stopped by the same quiesce token
/// (_videoUploadStop) so "Delete everything" stops a clip mid-way as it does
/// a video. Only mp3s: a clip the encoder refused stays a wav on this PC.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>One clip at most this big - the relay's own limit (CLIP_MAX_BYTES).</summary>
    public const long DriverClipMaxBytes = 4L * 1024 * 1024;

    /// <summary>
    /// Whether drivers' clips can be sent at all: the relay set up, drivers'
    /// nights offered, and a badge key to name them by. The club's own
    /// "share drivers' clips" tick is the caller's to check.
    /// </summary>
    public bool CanShareDriverClips =>
        CanSharePodiumVideos
        && _settings.PublishDriverNightsToTheRelay
        && !string.IsNullOrWhiteSpace(_settings.RelayCheckInSecret);

    /// <summary>
    /// The query a clip goes up with (22 Oct 2026, driver experience):
    /// driver (badge id, required), label, and race when the session id is
    /// one the relay takes. Null when the driver id is not a badge id, as
    /// the relay would refuse it. Nothing else - never a transponder.
    /// </summary>
    internal static string? ClipQuery(string? driverId, string? label, string? raceId)
    {
        if (!DriverNight.IsADriverId(driverId))
        {
            return null;
        }

        var query = new List<string>
        {
            "driver=" + driverId,
            "label=" + Uri.EscapeDataString(Trim(label, 80)),
        };

        if (IsAVideoKey(raceId))
        {
            query.Add("race=" + raceId);
        }

        return string.Join("&", query);
    }

    /// <summary>
    /// Sends one driver's clip to their own night. driverName is only ever
    /// used in this PC's log.
    /// </summary>
    internal async Task<ClipUploadResult> UploadDriverClipAsync(
        string mp3Path,
        string driverId,
        string label,
        string? raceId,
        string driverName,
        CancellationToken cancel = default)
    {
        var target = _target;

        if (!CanShareDriverClips)
        {
            return new ClipUploadResult(null, null, GiveUp: true);
        }

        var query = ClipQuery(driverId, label, raceId);

        // Only mp3s go up: the relay sniffs for one, and a wav the encoder
        // left behind is ten times the size.
        if (query is null || !string.Equals(Path.GetExtension(mp3Path), ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return new ClipUploadResult(null, null, GiveUp: false);
        }

        // Stopped by QuiesceAsync as well as by the caller - see _videoUploadStop.
        CancellationTokenSource stop;

        lock (_gate)
        {
            if (_quiesced)
            {
                return new ClipUploadResult(null, null, GiveUp: true);
            }

            stop = _videoUploadStop ??= new CancellationTokenSource();
        }

        using var upload = CancellationTokenSource.CreateLinkedTokenSource(cancel, stop.Token);
        cancel = upload.Token;

        try
        {
            var file = new FileInfo(mp3Path);

            if (!file.Exists || file.Length == 0)
            {
                return new ClipUploadResult(null, null, GiveUp: false);
            }

            if (file.Length > DriverClipMaxBytes)
            {
                return new ClipUploadResult(null, $"{driverName}'s clip is too big for their night ({file.Length / (1024 * 1024)} MB)", GiveUp: false);
            }

            var club = Uri.EscapeDataString(target.Club);

            // One more try after a wait when the relay is busy with another
            // laptop's clips (429): uploads here go one at a time, so a
            // second refusal is worth saying rather than retrying for ever.
            for (var attempt = 0; ; attempt++)
            {
                var url = $"{target.Root}/api/v1/c/{club}/clip/{NewVideoKey()}?{query}";

                await using var stream = new FileStream(mp3Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
                request.Content = new StreamContent(stream, 1 << 16);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
                request.Content.Headers.ContentLength = file.Length;

                using var response = await VideoHttp.SendAsync(request, cancel).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return new ClipUploadResult(await ClipFrom(response, cancel).ConfigureAwait(false) ?? string.Empty, null, GiveUp: false);
                }

                var status = (int)response.StatusCode;
                var said = await ErrorFrom(response, cancel).ConfigureAwait(false);

                if (status == 429 && attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), cancel).ConfigureAwait(false);
                    continue;
                }

                // An older relay has no clip route at all (a bare 404); one
                // without a clip vault, or with nights closed, refuses every
                // clip the same way - so the rest of the race is not tried.
                if (status == 404 && said is null)
                {
                    return new ClipUploadResult(null, "the relay does not take drivers' clips yet - redeploy it (fly deploy --ha=false)", GiveUp: true);
                }

                return new ClipUploadResult(
                    null,
                    $"the relay did not take {driverName}'s clip: HTTP {status}{(said is null ? "" : " - " + said)}",
                    GiveUp: status is 401 or 403 or 404);
            }
        }
        catch (OperationCanceledException)
        {
            return new ClipUploadResult(null, null, GiveUp: true);
        }
        catch (Exception ex)
        {
            return new ClipUploadResult(null, $"could not send {driverName}'s clip: {ex.Message}", GiveUp: false);
        }
    }

    /// <summary>The relay's address for a clip that landed - its {"clip": "..."} - or null.</summary>
    private static async Task<string?> ClipFrom(HttpResponseMessage response, CancellationToken cancel)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);

            return doc.RootElement.TryGetProperty("clip", out var clip) && clip.ValueKind == JsonValueKind.String
                ? clip.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
