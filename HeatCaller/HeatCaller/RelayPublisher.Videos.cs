using System.Net.Http.Headers;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The podium's highlight videos, on their way to the relay (21 Oct 2026).
///
/// After a race <see cref="HighlightVideo"/> makes an upright, captioned
/// video for each driver it was asked to. The top three - and ONLY the top
/// three - are sent here, so the drivers can watch and download them on
/// their phones: from the QR code the Big Screen shows after the race, or
/// from their own night on the club's page. The relay refuses anything off
/// the podium as well, whatever sends it (videos.ts).
///
/// NOT THROUGH THE RESULTS QUEUE. A video is tens of megabytes, sent once,
/// and worth nothing if it turns up after the next race has been called -
/// the QR code is only on the screen until then. So it goes straight up on
/// a thread of its own, with a client of its own: the publisher's shared
/// one has a hard sixty-second ceiling, which a 30 MB upload over a track's
/// Wi-Fi can outlast.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>
    /// The app's one publisher, for Race Control's highlight videos to send
    /// the podium's through - set by MainForm when it makes it. Race Control
    /// is not handed the publisher otherwise, and this is all it needs of it.
    /// </summary>
    internal static RelayPublisher? ForVideos { get; set; }

    /// <summary>One video at most this big - the relay's own limit (VIDEO_MAX_BYTES).</summary>
    public const long PodiumVideoMaxBytes = 48L * 1024 * 1024;

    /// <summary>
    /// The uploads' own client. No redirects, as the publisher's: the token
    /// rides on every request. Five minutes, because a slow uplink is not a
    /// broken one.
    /// </summary>
    private static readonly HttpClient VideoHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(5),
        MaxResponseContentBufferSize = 64 * 1024,
    };

    /// <summary>Whether podium videos can be sent at all: the relay switched on and fully set up.</summary>
    public bool CanSharePodiumVideos => _settings.RelayEnabled && IsConfigured() && IsSafeForToken(_target.Root);

    /// <summary>
    /// The address a race's podium page will have - known before a single
    /// video is up, so the Big Screen can show the code at once. Null when
    /// the relay is not set up.
    /// </summary>
    public string? PodiumVideosPageUrl(string raceKey)
    {
        var target = _target;

        if (!CanSharePodiumVideos || !IsAVideoKey(raceKey))
        {
            return null;
        }

        return $"{target.Root}/c/{Uri.EscapeDataString(target.Club)}/videos/{raceKey}";
    }

    /// <summary>128 random bits, as hex: what names a race's page and each video on the relay.</summary>
    public static string NewVideoKey() => Guid.NewGuid().ToString("N");

    /// <summary>Whether a key is one the relay will take: 32 lower-case hex characters.</summary>
    public static bool IsAVideoKey(string? key) =>
        key is { Length: 32 } && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>
    /// Sends one podium video. Position 1 to 3 only - anything else is
    /// refused here, before a byte is read, as the relay would refuse it.
    /// </summary>
    /// <returns>The race page's address once the relay has it, or null (and a line in the log) when it did not go.</returns>
    public async Task<string?> UploadPodiumVideoAsync(
        string mp4Path,
        string raceKey,
        int position,
        string? driverId,
        string driverName,
        string raceLabel,
        CancellationToken cancel = default)
    {
        if (position is < 1 or > 3)
        {
            return null;
        }

        var target = _target;

        if (!CanSharePodiumVideos || !IsAVideoKey(raceKey))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(mp4Path);

            if (!file.Exists || file.Length == 0)
            {
                return null;
            }

            if (file.Length > PodiumVideoMaxBytes)
            {
                Log?.Invoke($"[video] {driverName}'s video is too big to put on the club's page ({file.Length / (1024 * 1024)} MB).");
                return null;
            }

            var query = new List<string>
            {
                "race=" + raceKey,
                "position=" + position,
                "name=" + Uri.EscapeDataString(Trim(driverName, 80)),
                "label=" + Uri.EscapeDataString(Trim(raceLabel, 80)),
            };

            if (DriverNight.IsADriverId(driverId))
            {
                query.Add("driver=" + driverId);
            }

            var club = Uri.EscapeDataString(target.Club);
            var url = $"{target.Root}/api/v1/c/{club}/video/{NewVideoKey()}?{string.Join("&", query)}";

            await using var stream = new FileStream(mp4Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StreamContent(stream, 1 << 16);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            request.Content.Headers.ContentLength = file.Length;

            using var response = await VideoHttp.SendAsync(request, cancel).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var said = await ErrorFrom(response, cancel).ConfigureAwait(false);

                Log?.Invoke((int)response.StatusCode == 404 && said is null
                    ? "[video] The relay does not take highlight videos yet - redeploy it (fly deploy --ha=false)."
                    : $"[video] The club's page did not take {driverName}'s video: HTTP {(int)response.StatusCode}{(said is null ? "" : " - " + said)}");

                return null;
            }

            return $"{target.Root}/c/{club}/videos/{raceKey}";
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[video] Could not put {driverName}'s video on the club's page: {ex.Message}");
            return null;
        }
    }

    private static string Trim(string? text, int max)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length <= max ? value : value[..max];
    }

    /// <summary>The relay's own words for a refusal - its {"error": "..."} - or null.</summary>
    private static async Task<string?> ErrorFrom(HttpResponseMessage response, CancellationToken cancel)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);

            return doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
