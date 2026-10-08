namespace HeatCaller;

/// <summary>
/// Whatever writes the commentary: the relay, or Ollama on this laptop.
///
/// There are two of these and they are not interchangeable in quality -
/// the relay writes through a model nobody could run beside a race, and
/// Ollama writes offline with nothing to set up but Ollama itself. They
/// ARE interchangeable in contract, and that is what this is for: every
/// call returns null rather than throwing, so CommentaryService never has
/// to know which one it is holding and never needs a second failure path.
///
/// The rule the whole feature rests on: commentary is the garnish. A brain
/// that is slow, absent, out of allowance or broken must cost a race night
/// nothing but the colour, and every implementation here must keep that
/// true on its worst day.
/// </summary>
public interface ICommentaryBrain
{
    /// <summary>
    /// Asks for a single line. Null on any failure at all - timeout,
    /// refusal, bad JSON, empty answer - so callers only ever need one
    /// fallback path.
    /// </summary>
    /// <remarks>
    /// The defaults are declared HERE as well as on each implementation,
    /// and they have to be: a default on a class method is not used when
    /// the call goes through the interface, so an interface without them
    /// makes every argument required. The live talking point asks for no
    /// timeout on purpose - it wants the brain's own short one, where a
    /// recap between races can afford to wait - and it would not compile
    /// against a declaration that insisted.
    /// </remarks>
    Task<string?> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature = 0.7,
        CancellationToken ct = default,
        TimeSpan? timeout = null);

    /// <summary>
    /// Gets ready, where that means anything. Ollama loads the model;
    /// the relay checks it is there and the club is included. Never
    /// throws, and nothing waits on it.
    /// </summary>
    Task WarmUpAsync(CancellationToken ct = default);

    /// <summary>The Test button on the Commentary page. Null when it did not work.</summary>
    Task<string?> TestAsync(CancellationToken ct = default);

    /// <summary>Diagnostic detail for the console - a misconfigured brain has to be findable.</summary>
    event Action<string>? StatusMessage;

    /// <summary>One short phrase naming this brain, for the console and the page: "the relay" or "Ollama (llama3.2:3b)".</summary>
    string Describe { get; }
}
