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
    /// <remarks>
    /// Settable so a test can stand a fake relay behind it, exactly as
    /// HeatCallerVoiceClient.Http lets the voice's tests do. The app never
    /// sets it.
    /// </remarks>
    internal static HttpClient Http { get; set; } = new(new HttpClientHandler { AllowAutoRedirect = false })
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

    // ------------------------------------------------------------------
    // WHEN THE MONTH'S LINES ARE USED UP (19 Oct 2026)
    //
    // The relay refuses every line past the club's monthly allowance with
    // 429 "allowance", and until today each one was still sent to be
    // refused: a round trip per segment, a red console line per segment,
    // and - with the local box unticked - an evening with no commentary at
    // all. Now the month being spent is known (UsageMeters, which every
    // answer here reports into, and which also hears the relay's usage
    // figures directly), and while it is, nothing is sent: the line comes
    // back null at once and the brain behind this one writes it (see
    // FallbackCommentaryBrain). One probe goes through every half hour in
    // case lines were added that the store has not heard about yet, and
    // the relay is back in front the moment the store shows lines left -
    // a top-up - or the month's reset time has passed.
    // ------------------------------------------------------------------

    /// <summary>How often one line is let through to a spent relay anyway, in case a top-up has landed that nothing here has heard of yet.</summary>
    internal static readonly TimeSpan SpentProbeEvery = TimeSpan.FromMinutes(30);

    private readonly object _spentGate = new();

    /// <summary>When the next line may go through to a spent relay. Null while it is not spent. Under _spentGate.</summary>
    private DateTime? _nextProbeUtc;

    /// <summary>Whether the console has been told the month is spent, so it is told once - and told again when the relay is back. Under _spentGate.</summary>
    private bool _spentSaid;

    /// <summary>
    /// Set by an "allowance" refusal that carried no figures to report -
    /// which today's relay always sends, but an older one may not. Holds the
    /// month spent until the store shows a real figure with lines left, or
    /// the relay writes again. Under _spentGate.
    /// </summary>
    private bool _refusedWithoutFigures;

    /// <summary>The clock, for the probe and the reset time. Settable so a test can move it on half an hour without waiting.</summary>
    internal Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;

    /// <summary>
    /// Whether something behind this client will write when the month is
    /// spent - the model on this PC, built and not found too slow. Asked
    /// only for what the console says; set by whoever builds the pair.
    /// </summary>
    internal Func<bool>? LocalTakesOver { get; set; }

    /// <summary>
    /// True while this club's relay lines for the month are used up, as far
    /// as anything here knows: the store's commentary meter says Spent and
    /// its reset time has not passed, or the relay refused for the month
    /// without saying its figures and nothing since has said otherwise.
    /// </summary>
    public bool MonthSpent
    {
        get
        {
            // The store is read before the gate is taken, never under it:
            // nothing here should wait on another class's lock while
            // holding its own.
            var meter = UsageMeters.Shared.Get(MeterKind.Commentary);

            lock (_spentGate)
            {
                return IsSpent(UtcNow(), meter);
            }
        }
    }

    /// <summary>The month's allowance in lines, as the store last heard it - for "This month's 400 relay lines are used up". Null when it hasn't.</summary>
    public long? MonthAllowance =>
        UsageMeters.Shared.Get(MeterKind.Commentary) is { Known: true, Allowance: > 0 } meter ? meter.Allowance : null;

    /// <summary>MonthSpent's body, given the store's meter as it was just read. Called with _spentGate held.</summary>
    private bool IsSpent(DateTime now, Meter? meter)
    {
        // Without a reset date (an older relay's answer), a figure heard in
        // an earlier UTC month says nothing about this one: the relay's
        // months turn at midnight UTC on the 1st, so it is not held spent
        // past that - or a laptop opened on the 2nd would skip the relay
        // until a probe, half an hour in, found the new month.
        if (meter is { Spent: true }
            && (meter.ResetsUtc is { } resets
                ? now < resets
                : meter.AsOfUtc.Year == now.Year && meter.AsOfUtc.Month == now.Month))
        {
            return true;
        }

        // A real figure with lines left wins over a refusal that had none.
        if (meter is { Known: true, Allowance: > 0, Spent: false })
        {
            _refusedWithoutFigures = false;
        }

        return _refusedWithoutFigures;
    }

    /// <summary>
    /// Whether this line should skip the relay because the month is spent -
    /// false when it isn't, or when it is this half hour's probe. Says so in
    /// the console once each way.
    /// </summary>
    private bool SkipWhileSpent()
    {
        string? say = null;
        bool skip;

        // Read before the gate - see MonthSpent - and the sentence made
        // ready the same way, in case this is the first line skipped.
        var meter = UsageMeters.Shared.Get(MeterKind.Commentary);
        var sentence = SpentSentence(meter);

        lock (_spentGate)
        {
            var now = UtcNow();

            if (!IsSpent(now, meter))
            {
                if (_spentSaid)
                {
                    say = "[brain] The relay has lines again this month - it's writing the commentary.";
                }

                _spentSaid = false;
                _nextProbeUtc = null;
                skip = false;
            }
            else
            {
                if (!_spentSaid)
                {
                    _spentSaid = true;
                    say = sentence;
                }

                _nextProbeUtc ??= now + SpentProbeEvery;

                if (now >= _nextProbeUtc)
                {
                    // This half hour's one line through. Claimed here, under
                    // the gate, so two lines asked at once don't both go.
                    _nextProbeUtc = now + SpentProbeEvery;
                    skip = false;
                }
                else
                {
                    skip = true;
                }
            }
        }

        if (say is not null)
        {
            StatusMessage?.Invoke(say);

            // The night's budget hears the spent month too, the first time
            // a line is skipped for it. Nothing else would tell it: a month
            // the store already knew was spent - from the warm-up, or from
            // the "This month" refresh - sends no line to the relay, so no
            // answer ever carries the figures to AllowanceSeen, and the
            // budget would pace (and gate the relay-only segments) as if
            // it had never heard of the month.
            if (skip && meter is { Known: true, Allowance: > 0 } known)
            {
                TellAllowance(
                    (int)Math.Min(int.MaxValue, known.Used),
                    (int)Math.Min(int.MaxValue, known.Allowance));
            }
        }

        return skip;
    }

    /// <summary>
    /// The relay has just said the month is spent: start the half-hour wait
    /// for the next probe, and tell the console if it hasn't been told.
    /// </summary>
    private void MarkSpent(bool hadFigures)
    {
        string? say = null;
        var sentence = SpentSentence(UsageMeters.Shared.Get(MeterKind.Commentary));

        lock (_spentGate)
        {
            if (!hadFigures)
            {
                _refusedWithoutFigures = true;
            }

            _nextProbeUtc = UtcNow() + SpentProbeEvery;

            if (!_spentSaid)
            {
                _spentSaid = true;
                say = sentence;
            }
        }

        if (say is not null)
        {
            StatusMessage?.Invoke(say);
        }
    }

    /// <summary>The relay wrote a line, so whatever was thought about the month is out of date.</summary>
    private void MarkWriting()
    {
        lock (_spentGate)
        {
            // _spentSaid is left for SkipWhileSpent, which says "lines
            // again" once the store agrees - a write that took the last
            // line of the month leaves it spent.
            _refusedWithoutFigures = false;
            _nextProbeUtc = null;
        }
    }

    /// <summary>"This month's relay lines are used up - the model on this PC is writing until 1 Nov."</summary>
    private string SpentSentence(Meter? meter)
    {
        var until = meter?.ResetsUtc is { } resets
            ? $"until {resets.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)}"
            : "until the relay has lines again";

        bool local;

        try
        {
            local = LocalTakesOver?.Invoke() == true;
        }
        catch
        {
            local = false;
        }

        return local
            ? $"[brain] This month's relay lines are used up - the model on this PC is writing {until}."
            : $"[brain] This month's relay lines are used up - normal announcements carry on {until}.";
    }

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
    public Task<string?> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature = 0.7,
        CancellationToken ct = default,
        TimeSpan? timeout = null) =>
        GenerateCoreAsync(systemPrompt, userPrompt, maxTokens, temperature, ct, timeout, trimCutOff: true, askEvenIfSpent: false);

    /// <summary>
    /// The write itself. <paramref name="trimCutOff"/> is false only for the
    /// Test button: its answer is a bare "OK", which has no full stop and is
    /// under the twenty characters a kept sentence needs, so the cut-off
    /// check below threw away every correct answer and the button always
    /// said the relay had failed.
    ///
    /// <paramref name="askEvenIfSpent"/> is true only for the Test button
    /// too: somebody pressing it wants the relay's own answer, and a refusal
    /// for the month costs nothing - while a top-up the store has not heard
    /// of yet is found by it at once.
    /// </summary>
    private async Task<string?> GenerateCoreAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        CancellationToken ct,
        TimeSpan? timeout,
        bool trimCutOff,
        bool askEvenIfSpent)
    {
        if (Target() is not { } target)
        {
            StatusMessage?.Invoke("[brain] The Club Website page isn't set up, so there's no relay to write through.");
            return null;
        }

        // The month is spent: not sent just to be refused. See MonthSpent.
        if (!askEvenIfSpent && SkipWhileSpent())
        {
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

            var json = JsonSerializer.Serialize(payload);

            HttpStatusCode status;
            bool succeeded;
            string body;

            // BUSY IS "WAIT", NOT "NO" (18 Oct 2026). The relay writes a
            // limited number of lines for one club at a time and answers
            // the rest 429 "busy". On Air asks for the race intro, the
            // driver introductions and the Team Principal all at once, and
            // on 18 Oct the Team Principal was the one turned away - so the
            // first throw to pit lane of a demo was never heard. A busy
            // answer spends nothing, so it is asked again shortly, inside
            // the same timeout. Any other refusal is final, as before.
            for (var attempt = 1; ; attempt++)
            {
                TimeSpan? retryAfter;

                using (var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/brain/write")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                })
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

                    using var response = await Http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);

                    // Capped: a misconfigured address pointing at some other web
                    // server can answer with a whole page, and none of it is ours.
                    body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                    status = response.StatusCode;
                    succeeded = response.IsSuccessStatusCode;
                    retryAfter = response.Headers.RetryAfter?.Delta;
                }

                if (status == HttpStatusCode.TooManyRequests && attempt < BusyAttempts && IsBusy(body))
                {
                    var wait = retryAfter is { } asked
                        ? TimeSpan.FromMilliseconds(Math.Clamp(asked.TotalMilliseconds, 500, 3000))
                        : BusyWait;

                    StatusMessage?.Invoke(
                        $"[brain] The relay is busy with another line for this club - asking again in {wait.TotalSeconds:0.#}s.");

                    await Task.Delay(wait, timeoutCts.Token).ConfigureAwait(false);
                    continue;
                }

                break;
            }

            if (body.Length > MaxBodyChars)
            {
                StatusMessage?.Invoke($"[brain] The relay answered with {body.Length} characters, which is not a commentary line.");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!succeeded)
            {
                var why = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null;
                var refusal = root.TryGetProperty("refusal", out var kind) && kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;

                // THE MONTH IS SPENT, which is not a failure to shout about:
                // it is the one refusal with a plan behind it. Said in one
                // calm line (MarkSpent), not the red one below, and every
                // line after it skips the relay until there are lines
                // again. Only the "allowance" refusal means this - a bare
                // 429 is the relay's rate limit, and a 429 "busy" is a wait,
                // handled above - so the status alone is not trusted.
                if (string.Equals(refusal, "allowance", StringComparison.OrdinalIgnoreCase))
                {
                    var figures = Allowance(root);

                    MarkSpent(hadFigures: figures.Length > 0 && MonthSpent);
                    return null;
                }

                // Read before the message, never inside it: ?. does not
                // evaluate its arguments when nothing is listening, so a
                // figures-in-the-interpolation version tells UsageMeters
                // nothing on a PC with no console attached.
                //
                // WHY (20 Oct 2026): a relay deployed before today put a
                // made-up allowance of 1,000 lines on its "not-included"
                // (and "off") refusal, so the app showed a usage meter the
                // club never had, and it came and went. The relay no longer
                // sends those figures; this ignores them on a relay that
                // still does.
                var said = string.Equals(refusal, "not-included", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(refusal, "off", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : Allowance(root);

                StatusMessage?.Invoke(
                    $"[brain] The relay wouldn't write that one ({(int)status}{(refusal is null ? "" : $", {refusal}")}): "
                    + (why ?? "no reason given")
                    + said);

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
            //
            // THE FIGURES ARE READ FIRST, AND THAT IS THE WHOLE POINT (19 Oct
            // 2026). This was written as Invoke($"...{Allowance(root)}"), and
            // ?. does not evaluate its arguments when the event has no
            // subscriber - so on any PC with nothing listening to
            // StatusMessage, a successful write told UsageMeters nothing. The
            // month therefore stayed as the last refusal left it: spent, with
            // the relay skipped line after line, until something else
            // happened to report a figure. The Test button could not clear it
            // either, which is how it was found.
            var usage = Allowance(root);

            StatusMessage?.Invoke($"[brain] Written by the relay.{usage}");
            MarkWriting();

            var written = text.Trim();

            // A RELAY ANSWER CAN BE CUT OFF TOO (14 Oct 2026). The local
            // brain has trimmed a truncated answer back to its last whole
            // sentence since it was written - a model that runs past its
            // length limit stops mid-clause, and the announcer then reads
            // "...but let's be CRYSTAL CLEAR - this" and stops. The relay
            // path never did. On 14 Oct a recap went to air exactly like
            // that, and the show block then waited sixty seconds for a line
            // that had already run long, gave up, and took the music bed and
            // the "coming up" segment with it.
            //
            // No done_reason comes back from the relay, so the tell is the
            // text itself: a finished line ends on a full stop, a question
            // mark or an exclamation. One that does not was cut.
            //
            // Not for a two-hander: its turns are split on their A:/B: tags
            // and a half-written last turn is dropped there, where the
            // structure is understood. Trimming by sentence here would take
            // a whole turn off a conversation that was perfectly fine.
            var finished = Finish(written, userPrompt, maxTokens, trimCutOff, out var cutOff);

            if (cutOff)
            {
                StatusMessage?.Invoke(finished is null
                    ? "[brain] The relay's answer was cut off before it finished a sentence - not used."
                    : "[brain] The relay's answer was cut off - kept it up to the last complete sentence.");
            }
            else if (finished is null)
            {
                StatusMessage?.Invoke("[brain] The relay's line was empty or too long once tidied - not used.");
            }

            return finished;
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

    /// <summary>How many times one line is asked for while the relay says it is busy - see GenerateCoreAsync.</summary>
    private const int BusyAttempts = 8;

    /// <summary>How long to wait before asking a busy relay again, when it does not say.</summary>
    private static readonly TimeSpan BusyWait = TimeSpan.FromSeconds(1.5);

    /// <summary>Whether a refusal is the relay's "busy" - writing another line for this club right now - and so worth asking again.</summary>
    private static bool IsBusy(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);

            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("refusal", out var kind)
                   && kind.ValueKind == JsonValueKind.String
                   && string.Equals(kind.GetString(), "busy", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
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

            // Read first, for the reason given on the write path above: the
            // warm-up's whole job is to let the store hear a spent month
            // before the first line is asked for, and ?. would skip it.
            var catalogue = on ? Allowance(root, tellListeners: false) : string.Empty;

            StatusMessage?.Invoke(on
                ? $"[brain] The relay will write commentary for this club.{catalogue}"
                : "[brain] Commentary isn't included for this club on the relay yet - Ollama or the template lines will do it.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[brain] Couldn't reach the relay to check commentary: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<string?> TestAsync(CancellationToken ct = default) =>
        GenerateCoreAsync(
            "You are testing a race announcer's connection. Reply with exactly: OK",
            "Reply with exactly: OK",
            maxTokens: 10,
            temperature: 0,
            ct: ct,
            timeout: TimeSpan.FromSeconds(20),
            trimCutOff: false,
            askEvenIfSpent: true);

    /// <summary>A relay answer is a line and a count. Anything much bigger is not this relay answering.</summary>
    private const int MaxBodyChars = 8_000;

    /// <summary>
    /// What is done to the relay's text before it is handed on. The cut-off
    /// rule is as described in GenerateCoreAsync. 17 Oct 2026, Bug Hunt 8,
    /// A08-5: the relay's line now goes through LlmText.CleanLine like both
    /// local brains' do, so a "Commentary:" label, wrapping quotes or an
    /// over-long answer no longer reach the speech queue. Not for the Test
    /// button (<paramref name="trimCutOff"/> false), whose bare "OK" is
    /// returned as written.
    /// </summary>
    internal static string? Finish(string text, string userPrompt, int maxTokens, bool trimCutOff, out bool cutOff)
    {
        var written = text.Trim();
        cutOff = false;

        if (!trimCutOff)
        {
            return written;
        }

        var tagged = LlmText.AsksForTaggedTurns(userPrompt);

        if (tagged || EndsFinished(written))
        {
            return LlmText.CleanLine(written, maxTokens, tagged);
        }

        cutOff = true;
        return LlmText.CleanLine(LlmText.UpToLastWholeSentence(written), maxTokens, tagged);
    }

    /// <summary>
    /// True when the text ends the way a finished line does - terminal
    /// punctuation, allowing for a closing quote or bracket after it.
    /// </summary>
    private static bool EndsFinished(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var ch = text[i];

            if (ch is '"' or '\'' or '\u201D' or '\u2019' or ')' or ']')
            {
                continue;
            }

            return ch is '.' or '!' or '?';
        }

        return false;
    }

    /// <summary>
    /// Reads the club's month off a relay answer - used, allowance and,
    /// where it says, when it starts again - from the top of a write's
    /// answer or from the "usage" object the catalogue carries. Hands it to
    /// AllowanceSeen and to UsageMeters (the "This month" meter, and what
    /// MonthSpent asks), and returns it for the console: " 37 of 400 lines
    /// used this month." - or nothing, when the relay didn't say.
    ///
    /// <paramref name="tellListeners"/> is false for the warm-up's
    /// catalogue: the store hears it (so a month already spent is known
    /// before the first line is asked for), but the night's budget is still
    /// paced from the first write's figures, as it always has been.
    /// </summary>
    private string Allowance(JsonElement root, bool tellListeners = true)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var from = root;

        if (!root.TryGetProperty("used", out _)
            && root.TryGetProperty("usage", out var usage)
            && usage.ValueKind == JsonValueKind.Object)
        {
            from = usage;
        }

        if (!from.TryGetProperty("used", out var used)
            || !from.TryGetProperty("allowance", out var allowance)
            || used.ValueKind != JsonValueKind.Number
            || allowance.ValueKind != JsonValueKind.Number
            || !used.TryGetInt32(out var spent)
            || !allowance.TryGetInt32(out var cap))
        {
            return string.Empty;
        }

        DateTime? resets = null;

        if (from.TryGetProperty("resetsAt", out var resetsAt)
            && resetsAt.ValueKind == JsonValueKind.String
            && DateTime.TryParse(
                resetsAt.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var when))
        {
            resets = when;
        }

        try
        {
            UsageMeters.Shared.Report(MeterKind.Commentary, spent, cap, resets);
        }
        catch
        {
            // The meter is for the club to look at. Failing to keep it must
            // never become a commentary failure.
        }

        if (tellListeners)
        {
            TellAllowance(spent, cap);
        }

        return $" {spent:N0} of {cap:N0} lines used this month.";
    }

    /// <summary>Raises AllowanceSeen, never letting a listener's failure out.</summary>
    private void TellAllowance(int spent, int cap)
    {
        try
        {
            AllowanceSeen?.Invoke(spent, cap);
        }
        catch
        {
            // A listener's failure must not become a commentary failure -
            // the same rule the status message follows.
        }
    }
}
