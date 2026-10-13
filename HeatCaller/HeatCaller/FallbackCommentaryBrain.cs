namespace HeatCaller;

/// <summary>
/// Two brains, tried in order: the relay first, Ollama on this laptop if
/// the relay didn't write the line.
///
/// Per LINE, not per settings change, and that is the whole point. A club
/// can have the relay set up and still not be included for commentary on
/// it; the relay can be out of allowance, mid-deploy, or on the far side
/// of a track Wi-Fi that dropped. Choosing a brain once when the page is
/// saved would make every one of those a silent evening. Choosing per line
/// means the club gets the better words when it can and the local ones
/// when it can't, and never notices the difference except in quality.
///
/// There is no third attempt and no retry. Both brains already return null
/// rather than throwing, both already have their own deadline, and a line
/// that arrives after the moment has passed is worse than no line at all -
/// see the timeout note on LocalLlmClient.
/// </summary>
public sealed class FallbackCommentaryBrain : ICommentaryBrain
{
    private readonly ICommentaryBrain _first;
    private readonly ICommentaryBrain _second;

    private static readonly AsyncLocal<bool> _relayOnly = new();

    /// <summary>
    /// True while the line being asked for is one the second brain must not
    /// write - a colour or flourish segment, which CommentaryBudget says is
    /// better unsaid than written by a small model on the race laptop (see
    /// BrainFallback.None).
    ///
    /// Ambient rather than a parameter, so it reaches this class through
    /// CommentaryService without every brain and every prompt builder
    /// growing an argument only this one reads. An AsyncLocal, so it
    /// belongs to the one segment's call and not to whatever else is
    /// writing at the same moment - a race intro written while a cold open
    /// is in flight still falls back. Set by CommentaryDirector around the
    /// write and cleared after it.
    /// </summary>
    public static bool RelayOnly
    {
        get => _relayOnly.Value;
        set => _relayOnly.Value = value;
    }

    public FallbackCommentaryBrain(ICommentaryBrain first, ICommentaryBrain second)
    {
        _first = first;
        _second = second;

        // Both brains' console lines pass through, already tagged by the
        // brain that wrote them ("[brain] ..." and "[llm] ..."), so an
        // evening where the relay quietly handed over to Ollama reads as
        // what it was rather than as nothing happening.
        _first.StatusMessage += Say;
        _second.StatusMessage += Say;
    }

    /// <inheritdoc />
    public event Action<string>? StatusMessage;

    /// <inheritdoc />
    public string Describe => $"{_first.Describe}, falling back to {_second.Describe}";

    private void Say(string message) => StatusMessage?.Invoke(message);

    /// <inheritdoc />
    public async Task<string?> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature = 0.7,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var line = await _first
            .GenerateAsync(systemPrompt, userPrompt, maxTokens, temperature, ct, timeout)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(line))
        {
            return line;
        }

        // Not when the caller called it off. A round intro abandoned
        // because the next race was staged must not start a second,
        // slower attempt at a line nobody is waiting for any more.
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        if (RelayOnly)
        {
            Say($"[brain] Not falling back to {_second.Describe} - this segment is left unsaid rather than written locally.");
            return null;
        }

        Say($"[brain] Falling back to {_second.Describe}.");

        return await _second
            .GenerateAsync(systemPrompt, userPrompt, maxTokens, temperature, ct, timeout)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Warms both. Ollama's is the one that matters - it loads a model -
    /// and the relay's is a cheap question that also tells the console
    /// whether this club is included, which is the first thing anyone
    /// wants to know when commentary is on and nothing is being said.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        await _first.WarmUpAsync(ct).ConfigureAwait(false);
        await _second.WarmUpAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The Test button tries them in the same order a real line would, so
    /// what it reports is what a race night would actually get.
    /// </summary>
    public async Task<string?> TestAsync(CancellationToken ct = default)
    {
        var answer = await _first.TestAsync(ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(answer))
        {
            return answer;
        }

        return ct.IsCancellationRequested ? null : await _second.TestAsync(ct).ConfigureAwait(false);
    }
}
