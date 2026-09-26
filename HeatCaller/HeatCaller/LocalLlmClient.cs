using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Talks to a locally-running LLM for colour commentary.
///
/// Built against Ollama's REST API (https://github.com/ollama/ollama), which
/// is the least painful way to run a model on a race-day laptop: it's a
/// separate process the club installs once, so nothing ships inside this app
/// and there are no native ML dependencies to go wrong in the build.
///
/// Everything here is best-effort by design. The model may be slow, stopped,
/// or absent entirely, and none of those may disrupt a race - callers get
/// null back and fall through to the existing template lines. That's why
/// there's a hard timeout on every call rather than trusting the endpoint to
/// be responsive.
/// </summary>
public sealed class LocalLlmClient
{
    // Its own HttpClient with no BaseAddress, since the endpoint is a user
    // setting rather than a fixed service.
    private static readonly HttpClient Http = new();

    public required string Endpoint { get; init; }
    public required string Model { get; init; }

    /// <summary>
    /// How long Ollama keeps the model loaded after a request, in Ollama's
    /// own duration-string format (e.g. "30m"). Well beyond Ollama's 5-
    /// minute default so the model doesn't go cold again in the gap between
    /// races during an event - each request also renews this window, so as
    /// long as something is generated at least once every 30 minutes the
    /// model never has to reload.
    /// </summary>
    public string KeepAlive { get; init; } = "30m";

    /// <summary>
    /// Hard ceiling per request. Commentary that arrives after the moment
    /// has passed is worse than none, so a slow model is treated as a
    /// failure rather than something to wait for.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>Raised with diagnostic detail - surfaced in the console so a misconfigured endpoint is findable.</summary>
    public event Action<string>? StatusMessage;

    /// <summary>
    /// Asks the model for a single line. Returns null on any failure at all -
    /// timeout, connection refused, bad JSON, empty response - so callers
    /// only ever need one fallback path.
    /// </summary>
    /// <param name="timeout">
    /// Overrides the per-request ceiling. Race intros and recaps run between
    /// races with nothing waiting on them, so they can afford to wait far
    /// longer than a live talking point - and a small model's first answer
    /// after an idle spell is routinely slower than the live budget allows.
    /// </param>
    public async Task<string?> GenerateAsync(string systemPrompt, string userPrompt, int maxTokens, double temperature = 0.7, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(Endpoint) || string.IsNullOrWhiteSpace(Model))
        {
            return null;
        }

        var effectiveTimeout = timeout ?? Timeout;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            var payload = new
            {
                model = Model,
                prompt = userPrompt,
                system = systemPrompt,
                stream = false,
                // Keeps the model resident between calls rather than the
                // 5-minute default - without this, the gap between races at
                // a real event is routinely enough for it to unload and pay
                // the cold-load cost all over again.
                keep_alive = KeepAlive,
                options = new
                {
                    // This was silently hardcoded here even after the
                    // temperature parameter was added above it - the
                    // "phrasing variety" setting did nothing until this was
                    // wired to the actual argument instead of a constant.
                    temperature,
                    num_predict = maxTokens,
                },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint.TrimEnd('/')}/api/generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };

            using var response = await Http.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                // The body is where the actually useful detail lives - a 404
                // here almost always means the model hasn't been pulled, and
                // Ollama says exactly that ("model 'x' not found, try pulling
                // it first"). Throwing that away and showing only the status
                // code turns a self-explanatory error into a dead end.
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                // On a missing model, say which models ARE installed. "model
                // 'llama3.2:3b' not found" is accurate and still leaves you
                // guessing at the spelling of what you do have.
                //
                // Bounded by the same timeout as the request itself - this is
                // still inside a live call that has to give up on time.
                var installed = response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? await DescribeInstalledModelsAsync(timeoutCts.Token)
                    : string.Empty;

                StatusMessage?.Invoke($"[llm] Request failed ({(int)response.StatusCode}): {Truncate(body.Trim(), MaxLoggedBodyChars)}{installed}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);

            var text = doc.RootElement.TryGetProperty("response", out var r) ? r.GetString() : null;

            // The token budget is a safety net, not the length the model is
            // asked for - but a model that runs long hits it mid-sentence,
            // and the announcer then reads "...and we can't wait to" and
            // stops. Ollama says so ("done_reason": "length"); older builds
            // don't, but then the tokens it produced reach the budget.
            var stoppedAtLimit =
                (doc.RootElement.TryGetProperty("done_reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String
                    && string.Equals(reason.GetString(), "length", StringComparison.OrdinalIgnoreCase))
                || (doc.RootElement.TryGetProperty("eval_count", out var produced)
                    && produced.ValueKind == JsonValueKind.Number
                    && produced.TryGetInt32(out var producedCount)
                    && producedCount >= maxTokens);

            if (stoppedAtLimit && text is not null)
            {
                var whole = UpToLastWholeSentence(text);

                StatusMessage?.Invoke(whole is null
                    ? "[llm] The answer ran past its length limit before finishing a sentence - not used."
                    : "[llm] The answer ran past its length limit - kept it up to the last complete sentence.");

                text = whole;
            }

            return CleanLine(text, maxTokens);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"[llm] Timed out after {effectiveTimeout.TotalSeconds:0.#}s - falling back to templates.");
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[llm] {ex.GetType().Name}: {ex.Message} - falling back to templates.");
            return null;
        }
    }

    /// <summary>
    /// An error body goes straight into the console log. The useful part
    /// (Ollama's one-line reason) is at the start; a misconfigured endpoint
    /// pointing at some other web server can return a whole HTML page.
    /// </summary>
    private const int MaxLoggedBodyChars = 500;

    private static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + "... (truncated)";

    /// <summary>
    /// Asks Ollama what it has, for the "not found" message. Best effort -
    /// a failure here must never replace the real error with its own.
    /// </summary>
    private async Task<string> DescribeInstalledModelsAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint.TrimEnd('/')}/api/tags");
            using var response = await Http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("models", out var models))
            {
                return string.Empty;
            }

            var names = models.EnumerateArray()
                .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Take(12)
                .ToList();

            return names.Count == 0
                ? " Ollama is running but has no models installed - pull one with: ollama pull llama3.2:3b"
                : $" Models installed: {string.Join(", ", names)}. Put one of those in the Model box, or pull the one you want with: ollama pull {Model}";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Simple reachability check, for the settings page's Test button.</summary>
    public async Task<string?> TestAsync(CancellationToken ct = default) =>
        await GenerateAsync(
            "You are a test. Reply with exactly: OK",
            "Reply with exactly: OK",
            maxTokens: 10,
            ct: ct);

    /// <summary>
    /// Loads the model into memory without waiting for real use to trigger
    /// it. Ollama only loads a model on its first request, and that cold
    /// load is commonly well over the few seconds a live commentary call is
    /// given to answer - documented cases run 13-46+ seconds depending on
    /// model size. Calling this once when the client is (re)configured
    /// means an actual race's intro or summary isn't the first request that
    /// has to pay that cost.
    ///
    /// Deliberately far more patient than the client's own Timeout: this
    /// runs in the background with nothing waiting on it, so there's no
    /// reason to give up early the way a live call must.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Endpoint) || string.IsNullOrWhiteSpace(Model))
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));

        try
        {
            // An empty prompt loads the model and returns without generating
            // any real text - the standard idiom for pre-warming rather than
            // spending time on a throwaway completion.
            var payload = new { model = Model, prompt = "", stream = false, keep_alive = KeepAlive };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint.TrimEnd('/')}/api/generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };

            using var response = await Http.SendAsync(request, timeoutCts.Token);
            StatusMessage?.Invoke(response.IsSuccessStatusCode
                ? "[llm] Model warmed up and ready."
                : $"[llm] Warm-up request failed ({(int)response.StatusCode}).");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            StatusMessage?.Invoke("[llm] Warm-up timed out after 90s - the model may be unusually slow to load on this machine.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[llm] Warm-up failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Tidies a model response into something safe to speak.
    ///
    /// Small models routinely wrap output in quotes, prefix it with
    /// "Commentary:", or add a second sentence after being asked for one.
    /// Rather than fight that with prompt engineering alone, the worst of it
    /// is stripped here - and anything still too long is rejected outright,
    /// since an over-long line blocks the announcement queue behind it.
    /// </summary>
    /// <summary>
    /// The text up to and including its last complete sentence, or null
    /// when there isn't one worth speaking.
    ///
    /// A sentence ends at . ! or ? (with any closing quote or bracket after
    /// it) followed by a space or the end. A full stop between two digits
    /// is a decimal - "4.63 seconds" - not an ending.
    /// </summary>
    private static string? UpToLastWholeSentence(string text)
    {
        var trimmed = text.TrimEnd();

        for (var i = trimmed.Length - 1; i >= 0; i--)
        {
            var ch = trimmed[i];

            if (ch is not ('.' or '!' or '?'))
            {
                continue;
            }

            if (ch == '.' && i > 0 && i + 1 < trimmed.Length && char.IsDigit(trimmed[i - 1]) && char.IsDigit(trimmed[i + 1]))
            {
                continue;
            }

            var end = i + 1;

            while (end < trimmed.Length && trimmed[end] is '"' or '\'' or '\u201D' or '\u2019' or ')' or ']')
            {
                end++;
            }

            if (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
            {
                continue;
            }

            var kept = trimmed[..end].Trim();
            return kept.Length >= 20 ? kept : null;
        }

        return null;
    }

    private static string? CleanLine(string? text, int maxTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Trim();

        // Strip a leading label like "Commentary:" or "Announcer:".
        var colon = line.IndexOf(':');
        if (colon > 0 && colon < 16 && !line[..colon].Contains(' '))
        {
            line = line[(colon + 1)..].Trim();
        }

        line = line.Trim('"', '\u201C', '\u201D', '\'').Trim();

        // Collapse newlines into spaces - this is headed for a speech
        // queue, so paragraph breaks have no meaning, but the text itself
        // is kept. Longer settings legitimately produce multi-sentence
        // answers now.
        line = line.Replace('\r', ' ').Replace('\n', ' ');
        while (line.Contains("  "))
        {
            line = line.Replace("  ", " ");
        }

        // Scaled to what the caller actually asked for, with generous
        // headroom. This was a fixed 240 characters, which silently
        // REJECTED any longer answer and returned null - so raising the
        // word limit produced nothing at all rather than a longer line.
        var maxChars = Math.Max(240, maxTokens * 8);

        if (line.Length == 0 || line.Length > maxChars)
        {
            return null;
        }

        return line;
    }
}
