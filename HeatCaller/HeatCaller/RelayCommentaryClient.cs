using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Asks the club's HeatCaller relay to write a line of commentary.
///
/// The account is HeatCaller's, not the club's, so the key is on the relay
/// and nothing of it is here. It goes out on the club's own publish token
/// - the one the Club Website page already holds - so a club that has the
/// relay set up has this with nothing more to do, and a club that has no
/// relay simply carries on with Ollama or with the template lines.
///
/// WHY THIS EXISTS WHEN LocalLlmClient ALREADY DID THE JOB
///
/// Ollama works, and it stays (it is the fallback, and the only brain that
/// writes with the internet down). But it asked a club volunteer to install
/// a second program from a command prompt before the best feature in the
/// app would do anything, and the model small enough to run beside a race
/// on a club laptop wrote like one. Moving the writing to the relay costs
/// about a penny a race night and removes the setup entirely.
///
/// Everything here is best-effort, exactly as LocalLlmClient is: every
/// failure returns null and the caller falls through. See ICommentaryBrain.
/// </summary>
public sealed class RelayCommentaryClient : ICommentaryBrain
{
    /// <summary>
    /// A client of its own rather than RelayPublisher's, for the same
    /// reason the voice has one: a publish is a quick write, and a line
    /// written by a model is neither. No redirects - the publish token
    /// rides on every request and must not follow one anywhere.
    /// </summary>
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    private readonly AppSettings _settings;

    public RelayCommentaryClient(AppSettings settings) => _settings = settings;

    /// <inheritdoc />
    public event Action<string>? StatusMessage;

    /// <summary>
    /// The club's own line count for the month, as the relay reported it
    /// on its last answer - used, then the allowance.
    ///
    /// The relay has said this on every write since the brain went up; it
    /// only ever reached the console. CommentaryBudget paces the night
    /// against it, and it has to be the relay's figure rather than a tally
    /// kept here: a club that races off two laptops still has one month.
    /// Raised for a refusal as well as a success, which is the moment the
    /// number matters most.
    /// </summary>
    public event Action<int, int>? AllowanceSeen;

    /// <inheritdoc />
    public string Describe => "the relay";

    /// <summary>
    /// Hard ceiling per request when the caller names none. Longer than
    /// Ollama's default: this is a round trip to a hosted model rather
    /// than a process on the same machine, and a cold relay takes a moment.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>True when the Club Website page is set up enough to ask. Not a promise the club's plan includes commentary - only the relay knows that.</summary>
    public bool RelayConfigured => Target() is not null;

    /// <summary>
    /// Where this laptop's relay is, or null when there is nothing safe to
    /// ask. Deliberately the club's publish relay and only that: the voice
    /// may be pointed at a relay of its own on Voice Settings, but the
    /// words and the publishing belong to the same club and the same token.
    /// </summary>
    private (string Root, string Club, string Token)? Target()
    {
        var root = RelayPublisher.NormaliseBaseUrl(_settings.RelayBaseUrl);
        var club = (_settings.RelayClub ?? string.Empty).Trim().ToLowerInvariant();
        var token = (_settings.RelayToken ?? string.Empty).Trim();

        if (root.Length == 0 || club.Length == 0 || token.Length == 0 || !RelayPublisher.IsValidClubId(club))
        {
            return null;
        }

        // An address a restored backup brought in, not yet checked on the
        // page where it is shown: no token goes to it. See RestoredSettings.
        if (RestoredSettings.IsHeld(RestoredSettings.Part.PublishRelay))
        {
            return null;
        }

        // The token never leaves this process over plain http to anywhere
        // that is not this machine or a private address.
        if (!RelayPublisher.IsSafeForToken(root))
        {
            return null;
        }

        return (root, club, token);
    }

    /// <inheritdoc />
    public async Task<string?> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature = 0.7,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        if (Target() is not { } target)
        {
            StatusMessage?.Invoke("[brain] The Club Website page isn't set up, so there's no relay to write through.");
            return null;
        }

        var effectiveTimeout = timeout ?? Timeout;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            // The relay asks for a length in words, not tokens: it is what
            // the prompt asks the model for, and the token cap on its side
            // is only a safety net. Three words to four tokens, which is
            // the ratio the relay turns it back with.
            var words = Math.Max(10, (int)Math.Round(maxTokens * 0.75));

            var payload = new
            {
                system = systemPrompt,
                facts = userPrompt,
                words,
                variety = temperature,
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/brain/write")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = await Http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);

            // Capped: a misconfigured address pointing at some other web
            // server can answer with a whole page, and none of it is ours.
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (body.Length > MaxBodyChars)
            {
                StatusMessage?.Invoke($"[brain] The relay answered with {body.Length} characters, which is not a commentary line.");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!response.IsSuccessStatusCode)
            {
                var why = root.TryGetProperty("error", out var error) ? error.GetString() : null;
                var refusal = root.TryGetProperty("refusal", out var kind) ? kind.GetString() : null;

                StatusMessage?.Invoke(
                    $"[brain] The relay wouldn't write that one ({(int)response.StatusCode}{(refusal is null ? "" : $", {refusal}")}): "
                    + (why ?? "no reason given")
                    + Allowance(root));

                return null;
            }

            var text = root.TryGetProperty("text", out var line) ? line.GetString() : null;

            if (string.IsNullOrWhiteSpace(text))
            {
                StatusMessage?.Invoke("[brain] The relay answered with no line.");
                return null;
            }

            // Said once per line and only to the console: a club watching
            // its allowance should not have to ask the relay for it.
            StatusMessage?.Invoke($"[brain] Written by the relay.{Allowance(root)}");

            return text.Trim();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"[brain] The relay didn't answer within {effectiveTimeout.TotalSeconds:0.#}s - falling back.");
            return null;
        }
        catch (OperationCanceledException)
        {
            StatusMessage?.Invoke("[brain] Request called off before the relay answered.");
            return null;
        }
        catch (JsonException ex)
        {
            StatusMessage?.Invoke($"[brain] The relay's answer wasn't readable JSON: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[brain] {ex.GetType().Name}: {ex.Message} - falling back.");
            return null;
        }
    }

    /// <summary>
    /// The relay has nothing to load, so this only asks whether it would
    /// write at all - and says so in the console, where a club that turned
    /// commentary on and heard nothing will look first.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        if (Target() is not { } target)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/brain/catalogue");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = await Http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                StatusMessage?.Invoke("[brain] This relay is an older build that can't write commentary yet. Deploy the relay again, or use Ollama.");
                return;
            }

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode || body.Length > MaxBodyChars)
            {
                StatusMessage?.Invoke($"[brain] The relay wouldn't say whether commentary is on ({(int)response.StatusCode}).");
                return;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var on = root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;

            StatusMessage?.Invoke(on
                ? $"[brain] The relay will write commentary for this club.{Allowance(root)}"
                : "[brain] Commentary isn't included for this club on the relay yet - Ollama or the template lines will do it.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[brain] Couldn't reach the relay to check commentary: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<string?> TestAsync(CancellationToken ct = default) =>
        GenerateAsync(
            "You are testing a race announcer's connection. Reply with exactly: OK",
            "Reply with exactly: OK",
            maxTokens: 10,
            temperature: 0,
            ct: ct,
            timeout: TimeSpan.FromSeconds(20));

    /// <summary>A relay answer is a line and a count. Anything much bigger is not this relay answering.</summary>
    private const int MaxBodyChars = 8_000;

    /// <summary>" 37 of 400 lines used this month." - or nothing, when the relay didn't say.</summary>
    /// <summary>
    /// The club's month as the relay tells it, for the console - and, on
    /// the way past, for anything listening on AllowanceSeen.
    /// </summary>
    private string Allowance(JsonElement root)
    {
        if (!root.TryGetProperty("used", out var used)
            || !root.TryGetProperty("allowance", out var allowance)
            || used.ValueKind != JsonValueKind.Number
            || allowance.ValueKind != JsonValueKind.Number)
        {
            return string.Empty;
        }

        var spent = used.GetInt32();
        var cap = allowance.GetInt32();

        try
        {
            AllowanceSeen?.Invoke(spent, cap);
        }
        catch
        {
            // A listener's failure must not become a commentary failure -
            // the same rule the status message follows.
        }

        return $" {spent:N0} of {cap:N0} lines used this month.";
    }
}
