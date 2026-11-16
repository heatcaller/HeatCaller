namespace HeatCaller;

/// <summary>
/// Two brains, tried in order: the relay first, the model built into
/// HeatCaller on this PC if the relay didn't write the line.
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
///
/// TWO WAYS TO FALL BACK (19 Oct 2026). With "Let the model on this PC
/// write when the relay can't" ticked, any line the relay doesn't write
/// goes to the second brain, as it always has. Unticked, the second brain
/// is still here - but it writes only while the relay's lines for the
/// month are used up. A club that would rather a quiet segment than a
/// plainly-written one on a night the Wi-Fi drops did not choose an
/// evening with no commentary at all because the month ran out; that is
/// the switch-over the club asked for, and it needs nobody to find a tick
/// box mid-race.
/// </summary>
public sealed class FallbackCommentaryBrain : ICommentaryBrain
{
    private readonly ICommentaryBrain _first;
    private readonly ICommentaryBrain _second;

    /// <summary>True: any relay failure falls back (the box ticked). False: only a spent month does.</summary>
    private readonly bool _onAnyFailure;

    /// <summary>Whether the first brain's month is spent - RelayCommentaryClient.MonthSpent, unless a test hands in its own.</summary>
    private readonly Func<bool> _monthSpent;

    /// <summary>
    /// Whether the second brain has been warmed since this pair was built.
    /// In the spent-month-only mode it is not warmed with the first - see
    /// WarmUpAsync - so it is warmed the first time it is actually needed.
    /// 0 or 1, for Interlocked.
    /// </summary>
    private int _secondWarmed;

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

    private static readonly AsyncLocal<bool> _localOnly = new();

    /// <summary>
    /// True while the line being asked for is about a junior and the club has
    /// "Offline voices for juniors" on (15 Nov 2026, children's privacy, plan
    /// 2.3): the relay is not asked at all, and the model on this PC writes
    /// it - whichever way the fallback box is ticked. Set by
    /// CommentaryService.Ask around the one write, like RelayOnly; the relay
    /// client checks it too, for a club with no local model, and writes
    /// nothing rather than send the prompt.
    ///
    /// RelayOnly still wins: a colour segment about a junior is left unsaid
    /// rather than written plainly here.
    /// </summary>
    public static bool LocalOnly
    {
        get => _localOnly.Value;
        set => _localOnly.Value = value;
    }

    /// <param name="first">The relay.</param>
    /// <param name="second">The model on this PC.</param>
    /// <param name="onAnyFailure">True to fall back whenever the first doesn't write; false to fall back only while its month is spent.</param>
    /// <param name="monthSpent">Whether the first's month is spent. Defaults to asking the first, when it is the relay.</param>
    public FallbackCommentaryBrain(
        ICommentaryBrain first,
        ICommentaryBrain second,
        bool onAnyFailure = true,
        Func<bool>? monthSpent = null)
    {
        _first = first;
        _second = second;
        _onAnyFailure = onAnyFailure;
        _monthSpent = monthSpent ?? (() => first is RelayCommentaryClient { MonthSpent: true });

        // Both brains' console lines pass through, already tagged by the
        // brain that wrote them ("[brain] ..." and "[llm] ..."), so an
        // evening where the relay quietly handed over to the local model reads as
        // what it was rather than as nothing happening.
        _first.StatusMessage += Say;
        _second.StatusMessage += Say;
    }

    /// <inheritdoc />
    public event Action<string>? StatusMessage;

    /// <inheritdoc />
    public string Describe => _onAnyFailure
        ? $"{_first.Describe}, falling back to {_second.Describe}"
        : $"{_first.Describe}, with {_second.Describe} once the month's relay lines are used up";

    /// <summary>
    /// True when the second brain may write now that the month is spent -
    /// there is one, and it is not a built-in model already found unable to
    /// run or too slow on this PC. What CommentaryBudget asks before it
    /// stops rationing relay lines nobody is spending.
    /// </summary>
    public bool CoversSpentMonth =>
        _second is not BundledLlmClient bundled || (!bundled.TooSlow && bundled.Unavailable is null);

    /// <summary>Whether the first brain's month is spent and the second is taking over - see CoversSpentMonth.</summary>
    public bool TakingOver
    {
        get
        {
            try
            {
                return CoversSpentMonth && _monthSpent();
            }
            catch
            {
                return false;
            }
        }
    }

    private void Say(string message) => StatusMessage?.Invoke(message);

    /// <summary>Whether this failure is one the second brain covers: any, when ticked; only a spent month, when not.</summary>
    private bool ShouldFallBack(out bool spent)
    {
        try
        {
            spent = _monthSpent();
        }
        catch
        {
            spent = false;
        }

        return _onAnyFailure || spent;
    }

    /// <summary>
    /// Warms the second brain the first time a spent month hands it a line,
    /// when it was not warmed with the first. In the background, through the
    /// model's own one-at-a-time queue, so it measures this PC (TooSlow)
    /// rather than trying every line against a machine that cannot keep up.
    /// </summary>
    private void WarmSecondOnce()
    {
        if (Interlocked.Exchange(ref _secondWarmed, 1) == 0)
        {
            _ = _second.WarmUpAsync();
        }
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
        // About a junior, with "Offline voices for juniors" on: straight to
        // the model on this PC, never the relay - see LocalOnly.
        if (LocalOnly)
        {
            if (RelayOnly)
            {
                Say("[brain] A segment about a junior is left unsaid - it stays off the relay, and this kind of segment isn't written locally.");
                return null;
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            WarmSecondOnce();

            return await _second
                .GenerateAsync(systemPrompt, userPrompt, maxTokens, temperature, ct, timeout)
                .ConfigureAwait(false);
        }

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

        // Unticked and the relay simply didn't write this one - the Wi-Fi,
        // a timeout, a club not included: quieter, as the club chose.
        if (!ShouldFallBack(out var spent))
        {
            return null;
        }

        // Kept while the month is spent too: a colour or flourish segment is
        // still better unsaid than written plainly - see BrainFallback.None
        // and CommentaryBudget, which refuses those tiers once the month is
        // spent rather than letting them get this far.
        if (RelayOnly)
        {
            Say($"[brain] Not falling back to {_second.Describe} - this segment is left unsaid rather than written locally.");
            return null;
        }

        if (spent)
        {
            // Said once by the relay client ("This month's relay lines are
            // used up..."), not once a line here.
            WarmSecondOnce();
        }
        else
        {
            Say($"[brain] Falling back to {_second.Describe}.");
        }

        return await _second
            .GenerateAsync(systemPrompt, userPrompt, maxTokens, temperature, ct, timeout)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Warms both. The local one's is what matters - it loads the model,
    /// and the relay's is a cheap question that also tells the console
    /// whether this club is included, which is the first thing anyone
    /// wants to know when commentary is on and nothing is being said.
    ///
    /// Not the second in the spent-month-only mode while the month has lines:
    /// warming the built-in model loads several hundred megabytes and writes
    /// a timed line, and a club that unticked the box should not pay that on
    /// every start for a model that may not write all month. It is warmed
    /// here if the month is already spent (the relay's warm-up has just told
    /// the store), and otherwise the first time a spent month needs it.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        await _first.WarmUpAsync(ct).ConfigureAwait(false);

        if (_onAnyFailure || ShouldFallBack(out _))
        {
            Interlocked.Exchange(ref _secondWarmed, 1);
            await _second.WarmUpAsync(ct).ConfigureAwait(false);
        }
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

        if (ct.IsCancellationRequested || !ShouldFallBack(out _))
        {
            return null;
        }

        return await _second.TestAsync(ct).ConfigureAwait(false);
    }
}
