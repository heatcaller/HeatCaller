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

    /// <param name="Label">
    /// What a list shows for it when not just its name - "Suggested by
    /// HeatCaller: Finley - Articulate Anchor" (24 Nov 2026). The name is
    /// still what is saved.
    /// </param>
    public sealed record VoiceInfo(string VoiceId, string Name, string PreviewUrl = "", string Label = "")
    {
        /// <summary>What a list shows: the label when there is one, else the name.</summary>
        public string Shown => string.IsNullOrEmpty(Label) ? Name : Label;
    }

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
        // A voice ElevenLabs said it no longer has, or won't let this key
        // use, isn't asked again this session - see NoteGoneVoice.
        if (GoneVoiceFor(profile) is { } gone)
        {
            throw gone;
        }

        var asked = AsksNotToLog(profile.ApiKey);
        int refused;

        using (var first = build(asked))
        {
            var response = await Http.SendAsync(first, completion, ct);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            // WHY (24 Nov 2026, Asher: ElevenLabs retires its Default voices
            // on 31 Dec 2026): a voice that isn't there any more came back as
            // 400 or 404 "voice_not_found", and a 400 was taken for the plan
            // turning the no-logging flag down - so every line made two
            // failing requests before Kokoro stood in, and said nothing that
            // named the voice. Now it is read for what it is, said once, and
            // that voice (only that one) isn't asked again this session.
            // Buffered first, so the caller can still read the body.
            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (VoiceRefusalOf((int)response.StatusCode, body) is { } why)
            {
                response.Dispose();
                throw NoteGoneVoice(profile, why, (int)response.StatusCode, body);
            }

            if (!asked || !FlagMayBeRefused((int)response.StatusCode))
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

    // ------------------------------------------------------------------
    //  A voice that can't speak on this key (24 Nov 2026)
    // ------------------------------------------------------------------

    /// <summary>Why ElevenLabs won't speak in one voice for this key.</summary>
    internal enum VoiceRefusal
    {
        /// <summary>The voice isn't there any more - retired, or deleted from the account (voice_not_found).</summary>
        NotFound,

        /// <summary>A Library voice on a free plan, which can't use them through the API.</summary>
        NeedsPaidPlan,
    }

    /// <summary>
    /// What a refused speech request says about the VOICE, or null when it
    /// says nothing about it (a bad key, the quota, the no-logging flag).
    /// Read from ElevenLabs' own status word in the body: "voice_not_found"
    /// on a 400 or 404, and "free_users_not_allowed" (or its message about
    /// Library voices) for a free plan.
    /// </summary>
    /// <remarks>
    /// The voice_not_found body is a real answer (24 Nov 2026): 404
    /// {"detail":{"type":"not_found","code":"voice_not_found",...,"status":"voice_not_found"}}.
    /// The free-plan wording has not been seen from ElevenLabs on this PC yet,
    /// so it is matched loosely - its status word, or "library voice" with
    /// "free" in the message - and on any refusal status, since it can come
    /// as a 401 or 402, which would otherwise be read as the key turned down.
    /// </remarks>
    internal static VoiceRefusal? VoiceRefusalOf(int status, string? body)
    {
        var text = body ?? string.Empty;

        if (status is 400 or 404 && text.Contains("voice_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return VoiceRefusal.NotFound;
        }

        if (status is >= 400 and < 500
            && (text.Contains("free_users_not_allowed", StringComparison.OrdinalIgnoreCase)
                || (text.Contains("library voice", StringComparison.OrdinalIgnoreCase) && text.Contains("free", StringComparison.OrdinalIgnoreCase))))
        {
            return VoiceRefusal.NeedsPaidPlan;
        }

        return null;
    }

    /// <summary>
    /// A speech request ElevenLabs turned down because of the voice itself.
    /// Its message keeps the shape every other refusal has ("... failed (404):
    /// body") so the console's reasons still read it; <see cref="Plain"/> is
    /// the sentence for people.
    /// </summary>
    internal sealed class VoiceGoneException(string voiceId, string voiceName, VoiceRefusal why, string message)
        : InvalidOperationException(message)
    {
        public string VoiceId { get; } = voiceId;

        public string VoiceName { get; } = voiceName;

        public VoiceRefusal Why { get; } = why;

        /// <summary>What to tell the club, in one sentence or two.</summary>
        public string Plain => PlainWords(VoiceName, Why);
    }

    /// <summary>
    /// The words for a voice that can't speak: "ElevenLabs no longer has the
    /// voice Charlie. Pick another on Voice > Cast." - or, for a Library voice
    /// on a free plan, that it needs a paid plan.
    /// </summary>
    internal static string PlainWords(string voiceName, VoiceRefusal why) => why == VoiceRefusal.NeedsPaidPlan
        ? $"Library voices need a paid ElevenLabs plan, so {voiceName} can't speak on this key. Pick another on Voice > Cast, or move the key's account to a paid plan."
        : $"ElevenLabs no longer has the voice {voiceName}. Pick another on Voice > Cast.";

    /// <summary>The voices refused this session, by key hash and voice id, with their name and why. Held in memory only, so a restart asks again.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Name, VoiceRefusal Why)> GoneVoices = new(StringComparer.Ordinal);

    /// <summary>Raised (from any thread) when a voice is first refused, so the Voice tab can say so.</summary>
    internal static event Action? GoneVoicesChanged;

    private static string GoneKey(string? apiKey, string? voiceId) => KeyHash(apiKey) + ":" + (voiceId ?? string.Empty).Trim();

    /// <summary>The name a voice is said by: its saved name, the retired voice's name, or its id.</summary>
    private static string NameOf(VoiceProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.VoiceName) ? profile.VoiceName.Trim()
        : RetiredVoices.Find(profile.VoiceId)?.Name ?? profile.VoiceId;

    /// <summary>Whether this profile's voice was refused earlier this session - its lines go straight to the stand-in.</summary>
    internal static bool IsVoiceGone(VoiceProfile profile) => IsVoiceGone(profile.ApiKey, profile.VoiceId);

    /// <summary>The same, by key and voice id - for the pickers.</summary>
    internal static bool IsVoiceGone(string? apiKey, string? voiceId) => GoneWhy(apiKey, voiceId) is not null;

    /// <summary>Why this voice was refused for this key this session, or null when it wasn't.</summary>
    internal static VoiceRefusal? GoneWhy(string? apiKey, string? voiceId) =>
        !string.IsNullOrWhiteSpace(voiceId) && GoneVoices.TryGetValue(GoneKey(apiKey, voiceId), out var gone) ? gone.Why : null;

    /// <summary>The refusal to throw for this profile's voice without asking, or null when it hasn't been refused.</summary>
    private static VoiceGoneException? GoneVoiceFor(VoiceProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.VoiceId) && GoneVoices.TryGetValue(GoneKey(profile.ApiKey, profile.VoiceId), out var gone)
            ? new VoiceGoneException(profile.VoiceId, gone.Name, gone.Why,
                $"ElevenLabs speech request failed ({(gone.Why == VoiceRefusal.NotFound ? 404 : 402)}): {PlainWords(gone.Name, gone.Why)}")
            : null;

    /// <summary>The voices refused this session, as the Voice tab says them - one sentence each.</summary>
    internal static IReadOnlyList<string> GoneVoiceWords() =>
        GoneVoices.Values.Select(v => PlainWords(v.Name, v.Why)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Forgets every refused voice. For tests.</summary>
    internal static void ForgetGoneVoices() => GoneVoices.Clear();

    /// <summary>
    /// Remembers a voice refused for this key, says so once in the console,
    /// and hands back the exception to throw.
    /// </summary>
    private static VoiceGoneException NoteGoneVoice(VoiceProfile profile, VoiceRefusal why, int status, string body)
    {
        var name = NameOf(profile);

        if (GoneVoices.TryAdd(GoneKey(profile.ApiKey, profile.VoiceId), (name, why)))
        {
            try
            {
                Notice?.Invoke($"[voice] {PlainWords(name, why)} Until HeatCaller restarts that voice isn't asked again: "
                    + "the free Kokoro voice says its lines if it is standing in.");
            }
            catch
            {
                // Saying so is a nicety.
            }

            try
            {
                GoneVoicesChanged?.Invoke();
            }
            catch
            {
                // As above.
            }
        }

        return new VoiceGoneException(profile.VoiceId, name, why, $"ElevenLabs speech request failed ({status}): {body}");
    }

    // ------------------------------------------------------------------
    //  Each line taken out of the account's history (24 Nov 2026)
    // ------------------------------------------------------------------

    /// <summary>The header every speech answer names its history item in.</summary>
    internal const string HistoryItemHeader = "history-item-id";

    /// <summary>The keys that may not delete from their history (no Speech history permission), by hash - asked once a session.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> KeysWithoutHistoryDelete = new(StringComparer.Ordinal);

    /// <summary>The deletes still under way, so a test can wait for them.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Task, byte> PendingDeletes = new();

    /// <summary>Waits for every delete under way. For tests.</summary>
    internal static async Task HistoryDeletesSettledAsync()
    {
        while (!PendingDeletes.IsEmpty)
        {
            await Task.WhenAll(PendingDeletes.Keys.ToArray()).ConfigureAwait(false);
        }
    }

    /// <summary>Forgets every key's missing permission. For tests.</summary>
    internal static void ForgetHistoryRefusals() => KeysWithoutHistoryDelete.Clear();

    /// <summary>Whether a refused delete says the key lacks the Speech history permission.</summary>
    internal static bool MissingHistoryPermission(int status, string? body) =>
        // ElevenLabs' real answer (24 Nov 2026): 401 {"detail":{"type":"authentication_error",
        // "message":"The API key you used is missing the permission speech_history_write ...",
        // "status":"missing_permissions"}}.
        status is 401 or 403
        && ((body ?? string.Empty).Contains("missing_permissions", StringComparison.OrdinalIgnoreCase)
            || (body ?? string.Empty).Contains("speech_history", StringComparison.OrdinalIgnoreCase)
            || (body ?? string.Empty).Contains("permission", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Takes the line this answer made out of the account's history, in the
    /// background - never on the way to speaking it. Only while "Ask
    /// ElevenLabs not to keep our lines" is ticked, only by the exact id
    /// ElevenLabs gave in this answer's history-item-id header (never by
    /// listing the history or matching words), and not for a key already
    /// found without the permission.
    /// </summary>
    /// <remarks>
    /// WHY (24 Nov 2026, Asher, plan step H): enable_logging=false was taken
    /// with a 200 and quietly ignored on a non-enterprise key, so every line
    /// - drivers' names in it - stayed in the club's ElevenLabs history. Every
    /// answer names its history item, with or without the flag, so the line
    /// is deleted by that id once its audio is in hand. A 404 is already gone.
    /// </remarks>
    private static void ForgetLineLater(string? apiKey, HttpResponseMessage response)
    {
        try
        {
            if (!AskNotToKeepLines()
                || string.IsNullOrWhiteSpace(apiKey)
                || !response.Headers.TryGetValues(HistoryItemHeader, out var values)
                || values.FirstOrDefault()?.Trim() is not { Length: > 0 } itemId
                || KeysWithoutHistoryDelete.ContainsKey(KeyHash(apiKey)))
            {
                return;
            }

            // The client as it is now, so a delete goes where its line went.
            var http = Http;
            var task = Task.Run(() => DeleteHistoryItemAsync(http, apiKey, itemId));
            PendingDeletes.TryAdd(task, 0);
            _ = task.ContinueWith(done => PendingDeletes.TryRemove(done, out _), TaskScheduler.Default);
        }
        catch
        {
            // Never in the way of a line.
        }
    }

    /// <summary>DELETE v1/history/{id}. Never throws; a key without the permission is remembered and said once.</summary>
    private static async Task DeleteHistoryItemAsync(HttpClient http, string apiKey, string itemId)
    {
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.elevenlabs.io/v1/history/{Uri.EscapeDataString(itemId)}");
            request.Headers.Add("xi-api-key", apiKey);

            using var response = await http.SendAsync(request, limit.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode || status == 404)
            {
                return;
            }

            if (status is 401 or 403)
            {
                var body = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);

                if (KeysWithoutHistoryDelete.TryAdd(KeyHash(apiKey), 0) && MissingHistoryPermission(status, body))
                {
                    Notice?.Invoke("[voice] " + NoHistoryPermissionWords);
                }
            }
        }
        catch
        {
            // Off the speaking path: a delete that didn't go through is tried
            // for the next line, and the line itself was already heard.
        }
    }

    /// <summary>Said once a session for a key that can't delete from its history.</summary>
    internal const string NoHistoryPermissionWords =
        "This ElevenLabs key can't remove lines from its history. Give it the Speech history permission on elevenlabs.io.";

    /// <summary>
    /// A streamed answer that takes its line out of the history once it is
    /// given back - played to the end, or stopped part way - since only then
    /// is the line finished on ElevenLabs' side.
    /// </summary>
    private sealed class ForgetWhenDone : HttpResponseMessage
    {
        private readonly HttpResponseMessage _inner;
        private Action? _done;

        public ForgetWhenDone(HttpResponseMessage inner, Action done)
            : base(inner.StatusCode)
        {
            _inner = inner;
            _done = done;
            Version = inner.Version;
            ReasonPhrase = inner.ReasonPhrase;
            RequestMessage = inner.RequestMessage;
            Content = inner.Content;

            foreach (var header in inner.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Exchange(ref _done, null)?.Invoke();
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public async Task<byte[]> SynthesizeAsync(VoiceProfile profile, string text, CancellationToken ct = default)
    {
        using var response = await SendSpeechAsync(
            profile, noLogging => SpeechRequest(profile, text, stream: false, noLogging), HttpCompletionOption.ResponseContentRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ElevenLabs speech request failed ({(int)response.StatusCode}): {body}");
        }

        var audio = await response.Content.ReadAsByteArrayAsync(ct);

        // The audio is in hand: the line comes out of the history (plan step H).
        ForgetLineLater(profile.ApiKey, response);

        return audio;
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

        // Out of the history once the stream is given back - played, or
        // stopped part way (plan step H, 24 Nov 2026). Only when there is an
        // item to take out, so an answer without one is handed on as it came.
        if (AskNotToKeepLines() && response.Headers.Contains(HistoryItemHeader))
        {
            var apiKey = profile.ApiKey;
            var answered = response;
            return new ForgetWhenDone(answered, () => ForgetLineLater(apiKey, answered));
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

        HttpResponseMessage answered;

        try
        {
            answered = await SendSpeechAsync(profile, Build, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (VoiceGoneException)
        {
            // Said once and remembered by SendSpeechAsync. No timings, as for
            // any refusal: the lines go one at a time, and those go straight
            // to the stand-in (24 Nov 2026).
            return null;
        }

        using var response = answered;

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // Made, so it is in the history whatever its shape: out it comes (plan step H).
        ForgetLineLater(profile.ApiKey, response);

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
