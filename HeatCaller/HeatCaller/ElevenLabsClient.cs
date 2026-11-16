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
    // Each line's own limit is set per request by AnnouncementService
    // (30 s for a race call, more for a long recap - SynthesisTimeoutFor),
    // so a hung request still lets its synthesis slot go in good time.
    /// <summary>
    /// The client lines are spoken with. Settable so a test can answer for
    /// ElevenLabs (15 Nov 2026); the app never sets it. A test's client
    /// needs the same BaseAddress, since speech requests are relative.
    /// </summary>
    internal static HttpClient Http { get; set; } = new()
    {
        BaseAddress = new Uri("https://api.elevenlabs.io/"),
        // A backstop only (21 Oct 2026): the real limit is per line, and
        // grows with the line - see AnnouncementService.SynthesisTimeoutFor.
        // HttpClient.Timeout is a hard ceiling no per-request token can
        // lift, so it has to sit above the longest of those.
        Timeout = TimeSpan.FromSeconds(160),
    };

    /// <summary>
    /// The client the plan is read with: ten seconds, because it is only ever
    /// asked for a meter, never on the way to speaking a line. Settable so the
    /// tests can answer for ElevenLabs; the app never sets it. Requests to it
    /// are absolute, so a test's client needs no BaseAddress.
    /// </summary>
    internal static HttpClient SubscriptionHttp { get; set; } = new() { Timeout = TimeSpan.FromSeconds(10) };

    public sealed record VoiceInfo(string VoiceId, string Name, string PreviewUrl = "");

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
        // Thirty seconds of its own (22 Oct 2026, bug hunt): the client's
        // ceiling is now 160 s for long lines, and "Loading voices..." on a
        // stalled network sat that long with its button off.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        ct = limit.Token;

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

                // The account's own preview clip (15 Nov 2026, driver crew):
                // a driver picking a crew voice on their phone hears it from
                // here, so a sample costs no characters.
                var preview = v.TryGetProperty("preview_url", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                if (!string.IsNullOrEmpty(id))
                {
                    voices.Add(new VoiceInfo(id, name ?? id, preview ?? string.Empty));
                }
            }
        }

        return voices;
    }

    /// <summary>
    /// Whether ElevenLabs is asked not to keep our lines (15 Nov 2026,
    /// children's privacy, plan 2.3) - AppSettings.ElevenLabsNoLogging, "Ask
    /// ElevenLabs not to keep our lines" on the Voice tab, on by default.
    /// Wired once by AnnouncerControl; until then, and in tests, it asks.
    /// </summary>
    internal static Func<bool> AskNotToKeepLines { get; set; } = () => true;

    /// <summary>
    /// Said once per key when its plan turns the flag down - see
    /// SendSpeechAsync. AnnouncerControl points it at the console.
    /// </summary>
    internal static Action<string>? Notice { get; set; }

    /// <summary>
    /// The keys whose plan turned enable_logging=false down this session.
    /// Held in memory only, so a plan upgraded later is asked again after a
    /// restart. Keyed by a hash rather than the key itself.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> KeysWithoutNoLogging = new(StringComparer.Ordinal);

    private static string KeyHash(string? apiKey) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(apiKey ?? string.Empty)));

    /// <summary>Whether this key's next request carries enable_logging=false.</summary>
    internal static bool AsksNotToLog(string? apiKey) =>
        AskNotToKeepLines() && !KeysWithoutNoLogging.ContainsKey(KeyHash(apiKey));

    /// <summary>Forgets every key's refusal. For tests.</summary>
    internal static void ForgetNoLoggingRefusals() => KeysWithoutNoLogging.Clear();

    /// <summary>
    /// One speech request with the no-logging flag while it is wanted - and,
    /// when ElevenLabs turns a request with it down as a bad request (400,
    /// 403, 422), the same request once more without it (15 Nov 2026).
    /// </summary>
    /// <remarks>
    /// WHY. ElevenLabs' zero-retention mode has been offered to some plans
    /// only (historically enterprise), and a plan without it may refuse the
    /// request outright. A flag call matters more than the flag, so it is
    /// asked again without; if that goes through, the key is remembered for
    /// this session and said once in the console. A request that fails both
    /// ways was failing for another reason, and nothing is remembered.
    /// </remarks>
    private static async Task<HttpResponseMessage> SendSpeechAsync(
        VoiceProfile profile, Func<bool, HttpRequestMessage> build, HttpCompletionOption completion, CancellationToken ct)
    {
        var asked = AsksNotToLog(profile.ApiKey);
        int refused;

        using (var first = build(asked))
        {
            var response = await Http.SendAsync(first, completion, ct);

            if (response.IsSuccessStatusCode || !asked || !FlagMayBeRefused((int)response.StatusCode))
            {
                return response;
            }

            refused = (int)response.StatusCode;
            response.Dispose();
        }

        using var second = build(false);
        var retry = await Http.SendAsync(second, completion, ct);

        if (retry.IsSuccessStatusCode && KeysWithoutNoLogging.TryAdd(KeyHash(profile.ApiKey), 0))
        {
            try
            {
                Notice?.Invoke(
                    $"[voice] ElevenLabs turned down \"don't keep our lines\" ({refused}) - this key's plan doesn't offer it. "
                    + "Lines are sent without it until HeatCaller restarts.");
            }
            catch
            {
                // Saying so is a nicety; the line is what matters.
            }
        }

        return retry;
    }

    /// <summary>The answers a plan without zero retention could give the flag.</summary>
    private static bool FlagMayBeRefused(int status) => status is 400 or 403 or 422;

    public async Task<byte[]> SynthesizeAsync(VoiceProfile profile, string text, CancellationToken ct = default)
    {
        using var response = await SendSpeechAsync(
            profile, noLogging => SpeechRequest(profile, text, stream: false, noLogging), HttpCompletionOption.ResponseContentRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ElevenLabs speech request failed ({(int)response.StatusCode}): {body}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// The same request as <see cref="SynthesizeAsync"/>, to the stream
    /// endpoint, answered as soon as the headers are in (10 Nov 2026, voice
    /// speed plan, phase 3). The body is raw 16-bit mono PCM at 22.05 kHz
    /// (StreamingVoiceLine.ElevenLabsFormat), read as it arrives. The caller
    /// disposes the response. Throws, like SynthesizeAsync, for a refusal.
    /// </summary>
    internal async Task<HttpResponseMessage> OpenSpeechStreamAsync(VoiceProfile profile, string text, CancellationToken ct = default)
    {
        var response = await SendSpeechAsync(
            profile, noLogging => SpeechRequest(profile, text, stream: true, noLogging), HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"ElevenLabs speech request failed ({(int)response.StatusCode}): {body}");
            }
        }

        return response;
    }

    /// <summary>One speech request - whole (the club's output format, MP3 by default) or streamed (always PCM, see OpenSpeechStreamAsync).</summary>
    private static HttpRequestMessage SpeechRequest(VoiceProfile profile, string text, bool stream, bool noLogging)
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

        var request = new HttpRequestMessage(HttpMethod.Post, SpeechUrl(profile, stream, noLogging));
        request.Headers.Add("xi-api-key", profile.ApiKey);
        request.Headers.Add("Accept", stream ? "audio/pcm" : "audio/mpeg");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        return request;
    }

    /// <summary>
    /// Where one speech request goes, relative to api.elevenlabs.io - with
    /// <c>enable_logging=false</c> on the end when <paramref name="noLogging"/>
    /// (15 Nov 2026, see SendSpeechAsync).
    /// </summary>
    internal static string SpeechUrl(VoiceProfile profile, bool stream, bool noLogging, string endpoint = "")
    {
        // output_format is a QUERY parameter, not a body field - and left
        // off entirely unless set, so a free-tier account never gets a
        // request for a format that needs a paid plan by default.
        // Escaped: the voice id comes from settings, and a stray '/', '?' or
        // '#' in it would otherwise silently retarget the request.
        var url = $"v1/text-to-speech/{Uri.EscapeDataString(profile.VoiceId)}{endpoint}";
        if (stream)
        {
            url += $"/stream?output_format={StreamingVoiceLine.ElevenLabsFormat}";
        }
        else if (!string.IsNullOrWhiteSpace(profile.OutputFormat))
        {
            url += $"?output_format={Uri.EscapeDataString(profile.OutputFormat)}";
        }

        if (noLogging)
        {
            url += (url.Contains('?') ? "&" : "?") + "enable_logging=false";
        }

        return url;
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

        var json = JsonSerializer.Serialize(payload);

        HttpRequestMessage Build(bool noLogging)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, SpeechUrl(profile, stream: false, noLogging, endpoint: "/with-timestamps"));
            request.Headers.Add("xi-api-key", profile.ApiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return request;
        }

        using var response = await SendSpeechAsync(profile, Build, HttpCompletionOption.ResponseContentRead, ct);

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
