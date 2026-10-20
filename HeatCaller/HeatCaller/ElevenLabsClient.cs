using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Minimal wrapper around the ElevenLabs REST API - just enough for this app:
/// listing voices and synthesizing speech. See
/// https://elevenlabs.io/docs/api-reference/text-to-speech for the real spec
/// this was built against.
/// </summary>
public sealed class ElevenLabsClient
{
    // 35 s rather than HttpClient's 100 s default: a spoken race call that
    // arrives more than a few seconds late is worthless, and a hung request
    // would otherwise hold one of AnnouncementService's two synthesis slots
    // (and anything queued behind it) for well over a minute.
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://api.elevenlabs.io/"),
        Timeout = TimeSpan.FromSeconds(35),
    };

    /// <summary>
    /// The client the plan is read with: ten seconds, because it is only ever
    /// asked for a meter, never on the way to speaking a line. Settable so the
    /// tests can answer for ElevenLabs; the app never sets it. Requests to it
    /// are absolute, so a test's client needs no BaseAddress.
    /// </summary>
    internal static HttpClient SubscriptionHttp { get; set; } = new() { Timeout = TimeSpan.FromSeconds(10) };

    public sealed record VoiceInfo(string VoiceId, string Name);

    /// <summary>Where the key's own ElevenLabs plan stands this month, in characters.</summary>
    public sealed record Subscription(long CharacterCount, long CharacterLimit, DateTime? ResetsUtc);

    /// <summary>
    /// GET v1/user/subscription: the characters used and allowed this month
    /// and when they start again. A key made without the "User: read"
    /// permission is refused with 401 - thrown as an HttpRequestException
    /// carrying that status, so the caller can say what to change rather
    /// than call the key broken (it still speaks perfectly well).
    /// </summary>
    public async Task<Subscription> GetSubscriptionAsync(string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/user/subscription");
        request.Headers.Add("xi-api-key", apiKey);

        using var response = await SubscriptionHttp.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // The status only - not the body, which can echo the request.
            throw new HttpRequestException(
                $"ElevenLabs wouldn't show the plan ({(int)response.StatusCode}).",
                null,
                response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("ElevenLabs answered with something other than a plan.");
        }

        static long Whole(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
                ? Math.Max(0, number)
                : 0;

        var resetUnix = Whole(root, "next_character_count_reset_unix");

        DateTime? resets = null;

        if (resetUnix > 0)
        {
            try
            {
                resets = DateTimeOffset.FromUnixTimeSeconds(resetUnix).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                // A number no date answers to: no reset date, rather than no plan.
            }
        }

        return new Subscription(Whole(root, "character_count"), Whole(root, "character_limit"), resets);
    }

    public async Task<List<VoiceInfo>> GetVoicesAsync(string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/voices");
        request.Headers.Add("xi-api-key", apiKey);

        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ElevenLabs voices request failed ({(int)response.StatusCode}): {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var voices = new List<VoiceInfo>();
        if (doc.RootElement.TryGetProperty("voices", out var voicesArray))
        {
            foreach (var v in voicesArray.EnumerateArray())
            {
                var id = v.GetProperty("voice_id").GetString();
                var name = v.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.IsNullOrEmpty(id))
                {
                    voices.Add(new VoiceInfo(id, name ?? id));
                }
            }
        }

        return voices;
    }

    public async Task<byte[]> SynthesizeAsync(VoiceProfile profile, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profile.VoiceId))
        {
            throw new InvalidOperationException("No ElevenLabs voice selected.");
        }

        // Built as a JsonObject rather than a fixed anonymous type so
        // language_code can be left out entirely when blank. It was
        // previously always sent, including as an empty string, on the
        // assumption (from third-party wrapper docs, not ElevenLabs' own
        // primary spec) that empty means "auto-detect" - if that assumption
        // was wrong for a given account or model, it would fail EVERY
        // request with a validation error, which is indistinguishable from
        // "voice never works" without checking the actual response body.
        // Omitting the field when unset is the same "no preference" outcome
        // without depending on how the API happens to treat an empty string.
        var payload = new System.Text.Json.Nodes.JsonObject
        {
            ["text"] = text,
            ["model_id"] = profile.ModelId,
            ["apply_text_normalization"] = string.IsNullOrWhiteSpace(profile.TextNormalization) ? "auto" : profile.TextNormalization,
            ["voice_settings"] = new System.Text.Json.Nodes.JsonObject
            {
                ["stability"] = profile.Stability,
                ["similarity_boost"] = profile.SimilarityBoost,
                ["style"] = profile.Style,
                ["use_speaker_boost"] = profile.SpeakerBoost,
                ["speed"] = profile.Speed,
            },
        };

        if (!string.IsNullOrWhiteSpace(profile.LanguageCode))
        {
            payload["language_code"] = profile.LanguageCode;
        }

        // output_format is a QUERY parameter, not a body field - and left
        // off entirely unless set, so a free-tier account never gets a
        // request for a format that needs a paid plan by default.
        // Escaped: the voice id comes from settings, and a stray '/', '?' or
        // '#' in it would otherwise silently retarget the request.
        var url = $"v1/text-to-speech/{Uri.EscapeDataString(profile.VoiceId)}";
        if (!string.IsNullOrWhiteSpace(profile.OutputFormat))
        {
            url += $"?output_format={Uri.EscapeDataString(profile.OutputFormat)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("xi-api-key", profile.ApiKey);
        request.Headers.Add("Accept", "audio/mpeg");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ElevenLabs speech request failed ({(int)response.StatusCode}): {body}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// Audio plus per-character timings.
    ///
    /// Characters is what the API says it actually spoke, which is not
    /// always the string that was sent - normalisation and audio tags can
    /// both change it - so callers locate their own text inside Characters
    /// rather than trusting their own indices.
    /// </summary>
    public sealed record TimedSpeech(byte[] Audio, IReadOnlyList<char> Characters, IReadOnlyList<double> StartSeconds, IReadOnlyList<double> EndSeconds);

    /// <summary>
    /// Synthesises one request and asks for character-level timings with it,
    /// so several lines rendered together can be cut apart exactly.
    ///
    /// Returns null rather than throwing when the endpoint isn't available
    /// for this account or model, or the response carries no alignment -
    /// that is a cue for the caller to render the lines separately, not an
    /// error worth surfacing.
    /// </summary>
    public async Task<TimedSpeech?> SynthesizeWithTimestampsAsync(VoiceProfile profile, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profile.VoiceId))
        {
            throw new InvalidOperationException("No ElevenLabs voice selected.");
        }

        var payload = new System.Text.Json.Nodes.JsonObject
        {
            ["text"] = text,
            ["model_id"] = profile.ModelId,
            ["apply_text_normalization"] = string.IsNullOrWhiteSpace(profile.TextNormalization) ? "auto" : profile.TextNormalization,
            ["voice_settings"] = new System.Text.Json.Nodes.JsonObject
            {
                ["stability"] = profile.Stability,
                ["similarity_boost"] = profile.SimilarityBoost,
                ["style"] = profile.Style,
                ["use_speaker_boost"] = profile.SpeakerBoost,
                ["speed"] = profile.Speed,
            },
        };

        if (!string.IsNullOrWhiteSpace(profile.LanguageCode))
        {
            payload["language_code"] = profile.LanguageCode;
        }

        var url = $"v1/text-to-speech/{Uri.EscapeDataString(profile.VoiceId)}/with-timestamps";
        if (!string.IsNullOrWhiteSpace(profile.OutputFormat))
        {
            url += $"?output_format={Uri.EscapeDataString(profile.OutputFormat)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("xi-api-key", profile.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("audio_base64", out var audioElement)
                || audioElement.GetString() is not { Length: > 0 } audioBase64)
            {
                return null;
            }

            // The raw alignment, not normalized_alignment: it describes the
            // text as sent, which is what the caller has to find its own
            // segments in.
            if (!doc.RootElement.TryGetProperty("alignment", out var alignment)
                || alignment.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var characters = ReadChars(alignment, "characters");
            var starts = ReadDoubles(alignment, "character_start_times_seconds");
            var ends = ReadDoubles(alignment, "character_end_times_seconds");

            if (characters.Count == 0 || characters.Count != starts.Count || characters.Count != ends.Count)
            {
                return null;
            }

            return new TimedSpeech(Convert.FromBase64String(audioBase64), characters, starts, ends);
        }
        catch
        {
            // A shape we don't recognise is the same as no timings at all.
            return null;
        }
    }

    private static List<char> ReadChars(JsonElement parent, string name)
    {
        var result = new List<char>();

        if (parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var s = item.GetString();
                result.Add(string.IsNullOrEmpty(s) ? ' ' : s[0]);
            }
        }

        return result;
    }

    private static List<double> ReadDoubles(JsonElement parent, string name)
    {
        var result = new List<double>();

        if (parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                result.Add(item.TryGetDouble(out var value) ? value : 0);
            }
        }

        return result;
    }
}
