using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The plumbing a file upload to the relay needs - today only a driver's own
/// race clip (RelayPublisher.Clips.cs).
/// </summary>
/// <remarks>
/// WHERE THIS CAME FROM. It was written for the podium's highlight videos
/// (21 Oct 2026, RelayPublisher.Videos.cs), and the clips were built on the
/// same route a day later. On 28 Oct 2026 Asher asked for the videos to come
/// off the relay - "Remove the podium videos for downloading from the relay
/// and just have locally" - so the video upload, the race page's address,
/// the Big Screen's QR code and forget-media went, and what the clips still
/// use was moved here under names that say what it is for now. The videos
/// themselves are still made after every race and stay in the race's
/// Highlights folder on this PC.
///
/// NOT THROUGH THE RESULTS QUEUE. A clip is a few megabytes, sent once, and
/// goes straight up on the recorder's background thread with a client of
/// its own: the publisher's shared one has a hard sixty-second ceiling,
/// which an upload over a track's Wi-Fi can outlast.
/// </remarks>
public sealed partial class RelayPublisher
{
    /// <summary>
    /// The app's one publisher, for Race Control to send drivers' clips
    /// through - set by MainForm when it makes it. Race Control is not handed
    /// the publisher otherwise, and this is all it needs of it.
    /// </summary>
    internal static RelayPublisher? ForClips { get; set; }

    /// <summary>
    /// The uploads' own client. No redirects, as the publisher's: the token
    /// rides on every request. Five minutes, because a slow uplink is not a
    /// broken one.
    /// </summary>
    private static readonly HttpClient UploadHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(5),
        MaxResponseContentBufferSize = 64 * 1024,
    };

    /// <summary>
    /// Canceled by QuiesceAsync to stop any upload on its way, then dropped
    /// so the next upload gets a fresh one. Guarded by _gate.
    /// </summary>
    // WHY (22 Oct 2026, bug hunt 9): "Delete everything" stopped the
    // results queue but not an upload mid-way. The relay throws such a file
    // away (410), but the bytes still went; now they stop.
    private CancellationTokenSource? _uploadStop;

    /// <summary>Whether anything can be uploaded at all: the relay switched on and fully set up.</summary>
    private bool CanUpload => _settings.RelayEnabled && IsConfigured() && IsSafeForToken(_target.Root);

    /// <summary>128 random bits, as hex: what names each upload on the relay.</summary>
    public static string NewUploadKey() => Guid.NewGuid().ToString("N");

    /// <summary>Whether a key is one the relay will take: 32 lower-case hex characters.</summary>
    public static bool IsAnUploadKey(string? key) =>
        key is { Length: 32 } && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

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
