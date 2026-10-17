using System.Diagnostics;
using System.Text;

using LLama;
using LLama.Common;
using LLama.Sampling;

namespace HeatCaller;

/// <summary>
/// Writes commentary with a small model running INSIDE this process, from a
/// file shipped beside HeatCaller.exe.
///
/// This is the offline brain, and "offline" is the whole design brief. The
/// relay writes better lines through a model no race laptop could run, and
/// it is what writes whenever the club has internet. This exists for the
/// night the track Wi-Fi is down - which is precisely why the model file is
/// BUNDLED rather than downloaded on first run. A club whose internet is
/// unreliable is the club that needs this most, and it is also the club that
/// would never successfully fetch it. See fetch-llm.cmd and the
/// PublishLocalModel target in the csproj.
///
/// It replaces <see cref="LocalLlmClient"/>, which needed Ollama: a separate
/// install, a command prompt, and "ollama pull" before the best feature in
/// the app did anything. Nobody at a club should have to do that. Ollama
/// stays available as a setting for people who already run it, and the two
/// are interchangeable through <see cref="ICommentaryBrain"/>.
///
/// Everything here is best-effort, exactly as the interface demands: every
/// path returns null rather than throwing, so CommentaryService never needs
/// a second failure path. Commentary is the garnish, and a brain that is
/// slow, missing or broken must cost a race night nothing but the colour.
/// </summary>
public sealed class BundledLlmClient : ICommentaryBrain, IDisposable
{
    /// <summary>
    /// What the model file is called wherever it lives.
    ///
    /// Deliberately the app's own name for it rather than the model's, so
    /// swapping which model ships is a change to fetch-llm.cmd and nothing
    /// else. The console says which file it loaded, so a swapped model is
    /// still findable.
    /// </summary>
    public const string ModelFileName = "commentary.gguf";

    /// <summary>
    /// Anything smaller than this is a broken download or a git-lfs pointer,
    /// not a model. Same reasoning as KokoroSpeech.SmallestRealModel: loading
    /// a truncated file fails in a way that is much harder to read than
    /// simply refusing it. The model shipped today is about 440 MB; this is
    /// set well below that so a deliberately smaller model still passes.
    /// </summary>
    private const long SmallestRealModel = 100L * 1024 * 1024;

    /// <summary>
    /// Below this much total RAM the model is not even attempted.
    ///
    /// The file is ~440 MB and the context costs more on top, and a track PC
    /// is also running the timing decoders, the voice and the big screen. A
    /// machine this small is better off with no colour than with HeatCaller
    /// swapping to disk mid-race.
    /// </summary>
    private const long LeastUsefulRam = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// How much room the prompt and answer get. The briefs carry a good deal
    /// of race fact, so this is not as small as a 0.6B model's own minimum
    /// would allow - but every token here costs RAM in the KV cache, and
    /// this is a race PC.
    /// </summary>
    private const uint ContextSize = 4096;

    /// <summary>
    /// Where a downloaded model lands, for the portable zip and for anyone
    /// swapping a bigger one in by hand. The bundled copy beside the exe is
    /// preferred; this is the fallback, the same two-path arrangement
    /// KokoroSpeech uses for the voice.
    /// </summary>
    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HeatCaller",
        "LocalModel");

    /// <summary>Beside HeatCaller.exe - what the installer puts there.</summary>
    internal static string BundledModelPath => Path.Combine(AppContext.BaseDirectory, ModelFileName);

    /// <summary>In LocalAppData - never written to by the publish.</summary>
    internal static string DownloadedModelPath => Path.Combine(Folder, ModelFileName);

    /// <summary>
    /// A model file named on the Commentary page, for a club with a strong
    /// PC that wants a bigger one. Empty on every normal install.
    /// </summary>
    public string? ModelPathOverride { get; init; }

    /// <summary>
    /// Hard ceiling per request, as LocalLlmClient has. Commentary that
    /// arrives after the moment has passed is worse than none, so a slow
    /// model is a failure rather than something to wait for.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How many threads inference may use. Half the machine's cores, at most
    /// four: this runs beside the decoder reads, the voice and the screen,
    /// and taking every core to write one sentence is how a lap gets missed.
    /// </summary>
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <inheritdoc />
    public event Action<string>? StatusMessage;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// ONE INFERENCE AT A TIME. This is not an optimisation; without it the
    /// app dies.
    ///
    /// Commentary is written concurrently by design - FallbackCommentaryBrain
    /// says so itself ("a race intro written while a cold open is in flight")
    /// - and that was free when the local brain was Ollama, because
    /// concurrency was a separate process's problem. Running two inferences
    /// at once in THIS process is not free: it thrashes the CPU the race is
    /// also using, every line blows its deadline, and llama.cpp eventually
    /// takes the whole app down with it. A race night that saw this got a
    /// recap that took 64.9 seconds against a 1.2-second baseline, two
    /// timeouts, and then a crash on the next race.
    ///
    /// So lines queue. A line still waiting when its deadline passes gives
    /// up and falls through to the templates, which is exactly what should
    /// happen to a line nobody is waiting for any more - commentary is the
    /// garnish, and the race comes first.
    ///
    /// STATIC, ONE PER PROCESS (14 Oct 2026). It was per client, but the
    /// weights it protects are per process, and the Commentary page builds
    /// a new client on every settings change and warms it up at once - so
    /// the new client's warm-up ran beside the old client's line, which is
    /// the very concurrency this exists to stop. Worse, a changed model file
    /// swaps the shared weights and disposes the old ones, and that swap
    /// happens inside this gate; with one gate per client it could free the
    /// weights out from under another client's executor still writing. One
    /// gate for the whole process means no line is ever being written while
    /// the weights are swapped.
    /// </summary>
    private static readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>
    /// Settings the weights were loaded with, kept so each call can make its
    /// own executor from them. A StatelessExecutor is a thin wrapper that
    /// builds a fresh context per call, so making one per line costs nothing
    /// and shares no state between lines.
    /// </summary>
    private ModelParams? _parameters;

    // ------------------------------------------------------------------
    // The weights are loaded ONCE PER PROCESS, not once per client.
    //
    // This is the fix for a bug worth describing, because the shape of it
    // will come back. The Commentary page rebuilds its brain whenever
    // anything changes, which was free for years: the local brain was an
    // HTTP client holding an endpoint string, and building a hundred of
    // them cost nothing. The built-in model broke that assumption without
    // changing the code that relied on it - every rebuild read 440 MB off
    // disk and held a second copy of the weights while the first was
    // collected. The console showed it plainly: "Loaded commentary.gguf"
    // five times in a minute from nothing but moving around the app.
    //
    // Caching per client only moved the problem, since whoever holds the
    // client decides how long it lives. Caching here ends it. A
    // LLamaWeights is immutable and read-only once loaded, and
    // StatelessExecutor makes a fresh context per call anyway, so every
    // client in the process can share one set safely - which is how
    // llama.cpp is meant to be used. Building a BundledLlmClient is cheap
    // again, which restores the assumption the whole page rests on.
    //
    // They are deliberately never released. ~500 MB resident for the
    // session is the right trade on a race PC: it is exactly what Ollama's
    // 30-minute keep_alive was emulating, and it means the first segment of
    // the night does not pay a cold load. Switching to a different model
    // file swaps them; nothing else unloads them.
    // ------------------------------------------------------------------
    private static readonly SemaphoreSlim LoadGate = new(1, 1);
    private static string? _loadedKey;
    private static LLamaWeights? _loadedWeights;

    /// <summary>
    /// Set when loading failed in a way that will not get better by trying
    /// again: no model file, not enough RAM, or the native library refusing
    /// to load on this CPU. Checked before every call so a machine that
    /// cannot run this pays the cost once rather than per line.
    /// </summary>
    private bool _unavailable;
    private string? _unavailableWhy;

    /// <summary>
    /// Set when the warm-up's own timed line came back slower than a live
    /// call is allowed to take. See <see cref="TooSlow"/>.
    /// </summary>
    private bool _tooSlow;
    private TimeSpan _measured;

    /// <summary>
    /// True when this machine has been measured and cannot write a line
    /// inside the live budget.
    ///
    /// This is the answer to not knowing what hardware a club has. Rather
    /// than guess a minimum specification and publish it, the warm-up writes
    /// one throwaway line and times it on the actual PC. A machine that
    /// misses the budget turns the local brain off and says so on the
    /// Commentary page - which is far better than every Core segment waiting
    /// the full timeout and then falling through to nothing anyway, all
    /// night, with no explanation.
    /// </summary>
    public bool TooSlow => _tooSlow;

    /// <summary>How long the measured line took. Zero until warm-up has run.</summary>
    public TimeSpan Measured => _measured;

    /// <summary>Why this machine cannot use the built-in model. Null when it can, or when nothing has been tried yet.</summary>
    public string? Unavailable => _unavailableWhy;

    /// <inheritdoc />
    public string Describe => _tooSlow
        ? "the built-in model (too slow on this PC)"
        : _unavailableWhy is not null
            ? $"the built-in model ({_unavailableWhy})"
            : "the built-in model";

    /// <summary>
    /// Which model file this will load, or null when there isn't one.
    /// Bundled first, then downloaded, with a hand-named override ahead of
    /// both.
    /// </summary>
    internal string? ResolveModelPath()
    {
        if (!string.IsNullOrWhiteSpace(ModelPathOverride) && IsRealModel(ModelPathOverride))
        {
            return ModelPathOverride;
        }

        if (IsRealModel(BundledModelPath))
        {
            return BundledModelPath;
        }

        return IsRealModel(DownloadedModelPath) ? DownloadedModelPath : null;
    }

    /// <summary>True when there is a model file big enough to be real.</summary>
    internal static bool IsModelAvailable =>
        IsRealModel(BundledModelPath) || IsRealModel(DownloadedModelPath);

    private static bool IsRealModel(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length >= SmallestRealModel;
        }
        catch
        {
            // An unreadable path is a missing model as far as this is
            // concerned - there is nothing a race night can do about it.
            return false;
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
        if (_unavailable || _tooSlow)
        {
            return null;
        }

        var effectiveTimeout = timeout ?? Timeout;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            var (text, hitLimit) = await RunGuardedAsync(
                systemPrompt,
                userPrompt,
                maxTokens,
                temperature,
                timeoutCts.Token).ConfigureAwait(false);

            // The token budget is a safety net, not the length asked for -
            // but a model that runs long stops mid-sentence, and the
            // announcer then reads "...and we can't wait to" and stops.
            if (hitLimit && text is not null)
            {
                var whole = LlmText.UpToLastWholeSentence(text);

                StatusMessage?.Invoke(whole is null
                    ? "[llm] The answer ran past its length limit before finishing a sentence - not used."
                    : "[llm] The answer ran past its length limit - kept it up to the last complete sentence.");

                text = whole;
            }

            // The brief's own shape decides how the answer may be tidied: a
            // tagged two-hander has to keep its tags and its line breaks.
            return LlmText.CleanLine(text, maxTokens, LlmText.AsksForTaggedTurns(userPrompt));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            StatusMessage?.Invoke($"[llm] Timed out after {effectiveTimeout.TotalSeconds:0.#}s - falling back to templates.");
            return null;
        }
        catch (OperationCanceledException)
        {
            // Called off by the caller - a round intro past its budget, or
            // the recap's interview once the next race is called. Not a
            // failure: the caller says why in its own words.
            StatusMessage?.Invoke("[llm] Request called off before the model answered.");
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[llm] {ex.GetType().Name}: {ex.Message} - falling back to templates.");
            return null;
        }
    }

    /// <summary>
    /// Waits its turn, then writes one line. Everything that reaches the
    /// model goes through here - see the note on _oneAtATime for why that
    /// matters more than it looks.
    /// </summary>
    /// <param name="timed">
    /// Warm-up's stopwatch, when it is measuring. Started only once the turn
    /// has come and the weights are loaded, so it times the writing alone.
    /// </param>
    private async Task<(string? Text, bool HitLimit)> RunGuardedAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        CancellationToken ct,
        Stopwatch? timed = null)
    {
        // The deadline covers the queueing as well as the writing, on
        // purpose: a line that spent its whole budget waiting for another
        // one is a line whose moment has passed.
        await _oneAtATime.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var ready = await GetWeightsAsync(ct).ConfigureAwait(false);

            if (ready is null)
            {
                return (null, false);
            }

            var (weights, parameters) = ready.Value;

            timed?.Restart();

            return await RunAsync(
                new StatelessExecutor(weights, parameters),
                systemPrompt,
                userPrompt,
                maxTokens,
                temperature,
                ct).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// Generates once and says whether it stopped because it ran out of
    /// tokens. Split out so warm-up can time a real line through exactly the
    /// same path a race night uses.
    /// </summary>
    private static async Task<(string? Text, bool HitLimit)> RunAsync(
        StatelessExecutor executor,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        CancellationToken ct)
    {
        var inference = new InferenceParams
        {
            MaxTokens = maxTokens,

            // The chat tags themselves, so generation stops at the end of
            // the assistant's turn instead of carrying on and inventing the
            // next one - which small models do constantly.
            AntiPrompts = new[] { "<|im_end|>", "<|im_start|>" },

            // Keeps the special tokens out of the text. They are stop
            // markers, not words, and they must never reach the voice.
            DecodeSpecialTokens = false,

            // A fresh pipeline per call: Temperature is init-only, and the
            // "phrasing variety" setting is a per-call argument.
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = (float)temperature,

                // Small models repeat themselves across a night far more
                // than a large one does - the same phrasing three heats
                // running is the thing most likely to break the illusion.
                RepeatPenalty = 1.1f,
            },
        };

        var built = new StringBuilder();
        var produced = 0;

        await foreach (var token in executor.InferAsync(BuildPrompt(systemPrompt, userPrompt), inference, ct).ConfigureAwait(false))
        {
            built.Append(token);
            produced++;
        }

        return (StripThinking(built.ToString()), produced >= maxTokens);
    }

    /// <summary>
    /// A pre-filled empty think block should stop this model reasoning
    /// out loud, but a model is not a contract: if a block appears
    /// anyway, the thinking is not commentary and must not be spoken.
    /// Everything up to a closing tag goes, and so does everything from a
    /// fresh opening tag on (17 Oct 2026, Bug Hunt 8, A08-3: an unclosed
    /// "&lt;think&gt;Okay the user wants..." went out as commentary).
    /// </summary>
    internal static string StripThinking(string text)
    {
        var close = text.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (close >= 0)
        {
            text = text[(close + "</think>".Length)..];
        }

        var open = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (open >= 0)
        {
            text = text[..open];
        }

        return text;
    }

    /// <summary>
    /// Wraps the brief in the chat format the model was trained on (ChatML),
    /// because a stateless executor is handed a raw string and applies no
    /// template of its own. Getting this wrong does not fail loudly - it
    /// just produces noticeably worse lines, which is the hardest kind of
    /// bug to spot in commentary.
    ///
    /// Two things stop the model reasoning out loud. Qwen3 is a hybrid
    /// model: left alone it writes a &lt;think&gt; block first, which would eat
    /// the entire token budget before a word of commentary. "/no_think" is
    /// the documented switch, and pre-filling an already-closed think block
    /// in the assistant's turn is the deterministic one. Both are here on
    /// purpose; they cost nothing and they fail independently.
    /// </summary>
    private static string BuildPrompt(string systemPrompt, string userPrompt) =>
        $"<|im_start|>system\n{systemPrompt}<|im_end|>\n" +
        $"<|im_start|>user\n{userPrompt}\n\n/no_think<|im_end|>\n" +
        "<|im_start|>assistant\n<think>\n\n</think>\n\n";

    /// <summary>
    /// The executor, loading the model on first use. Guarded, so two lines
    /// asked for at once load it once rather than twice - which on a 440 MB
    /// model would be both slow and a way to run a small PC out of memory.
    /// </summary>
    private async Task<(LLamaWeights Weights, ModelParams Parameters)?> GetWeightsAsync(CancellationToken ct)
    {
        if (_loadedWeights is not null && _parameters is not null)
        {
            return (_loadedWeights, _parameters);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_loadedWeights is not null && _parameters is not null)
            {
                return (_loadedWeights, _parameters);
            }

            if (_unavailable)
            {
                return null;
            }

            var path = ResolveModelPath();

            if (path is null)
            {
                MarkUnavailable(IsAnyFileThere()
                    ? "the model file is there but too small to be real"
                    : "no model file is installed");
                return null;
            }

            // Asked before loading rather than discovered by failing: a
            // machine this small will swap rather than refuse, and swapping
            // mid-race is the worst outcome available.
            var ram = TotalRamBytes();

            if (ram > 0 && ram < LeastUsefulRam)
            {
                MarkUnavailable($"this PC has {ram / (1024 * 1024 * 1024.0):0.#} GB of memory, too little to run it beside a race");
                return null;
            }

            var parameters = new ModelParams(path)
            {
                ContextSize = ContextSize,

                // Explicitly none. The default is 20, which on the CPU
                // backend asks for an offload that cannot happen - and a
                // club PC's integrated graphics would be slower than the
                // CPU for a model this size even where it could.
                GpuLayerCount = 0,

                Threads = Threads,
                BatchThreads = Threads,
            };

            var weights = await SharedWeightsAsync(path, parameters, ct).ConfigureAwait(false);

            _parameters = parameters;

            return (weights, parameters);
        }
        catch (OperationCanceledException)
        {
            // Not a permanent failure - the caller gave up on this one line.
            throw;
        }
        catch (DllNotFoundException ex)
        {
            // The native library would not load. Overwhelmingly this is a
            // CPU too old for the instruction sets the backend ships, which
            // no amount of retrying will change.
            MarkUnavailable($"this PC's processor cannot run it ({ex.GetType().Name})");
            return null;
        }
        catch (TypeInitializationException ex)
        {
            MarkUnavailable($"the model engine would not start on this PC ({ex.InnerException?.Message ?? ex.Message})");
            return null;
        }
        catch (Exception ex)
        {
            MarkUnavailable($"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The process's one set of weights for this model file, loading them if
    /// nothing has yet. See the note on the static fields above for why this
    /// is not per client.
    /// </summary>
    private async Task<LLamaWeights> SharedWeightsAsync(string path, ModelParams parameters, CancellationToken ct)
    {
        // Threads and context size are in the key as well as the path: they
        // are baked into the loaded model, so a client asking for different
        // ones cannot be handed this copy.
        var key = string.Join('\u001f', path, ContextSize, Threads);

        if (_loadedKey == key && _loadedWeights is not null)
        {
            return _loadedWeights;
        }

        await LoadGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_loadedKey == key && _loadedWeights is not null)
            {
                return _loadedWeights;
            }

            // Synchronous load on a worker thread rather than the async
            // overload: this must not block the UI thread, and the
            // synchronous call is the one that has been in LLamaSharp
            // unchanged across versions.
            var weights = await Task.Run(() => LLamaWeights.LoadFromFile(parameters), ct).ConfigureAwait(false);

            // A different model than the one held - swapped on the
            // Commentary page. Let the old one go; nothing else can be
            // holding it, since every executor is rebuilt from this.
            var previous = _loadedWeights;

            _loadedWeights = weights;
            _loadedKey = key;

            if (previous is not null)
            {
                try
                {
                    previous.Dispose();
                }
                catch
                {
                    // Nothing useful to do if the native side objects.
                }
            }

            StatusMessage?.Invoke($"[llm] Loaded {Path.GetFileName(path)} on {Threads} thread(s).");

            return weights;
        }
        finally
        {
            LoadGate.Release();
        }
    }

    /// <summary>True when a file is sitting in one of the model places but failed the size floor.</summary>
    private bool IsAnyFileThere()
    {
        try
        {
            return File.Exists(BundledModelPath)
                || File.Exists(DownloadedModelPath)
                || (!string.IsNullOrWhiteSpace(ModelPathOverride) && File.Exists(ModelPathOverride));
        }
        catch
        {
            return false;
        }
    }

    private void MarkUnavailable(string why)
    {
        _unavailable = true;
        _unavailableWhy = why;
        StatusMessage?.Invoke($"[llm] The built-in model is not being used: {why}.");
    }

    /// <summary>
    /// What this machine has, in bytes, or 0 when it cannot be worked out.
    /// GC.GetGCMemoryInfo reports the memory this process may use, which on
    /// a normal Windows PC is its physical RAM - and needs no P/Invoke.
    /// </summary>
    private static long TotalRamBytes()
    {
        try
        {
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Loads the model and times one real line on this PC.
    ///
    /// Two jobs. The first is the same one Ollama's warm-up had: loading a
    /// model takes seconds, and a race's intro must not be the request that
    /// pays for it. The second is the measurement, and it is why this class
    /// can be shipped to hardware nobody has seen - rather than publishing a
    /// minimum specification and hoping, the app finds out what this
    /// particular PC can do and turns the feature off if the answer is "not
    /// enough". Runs in the background with nothing waiting on it, so it can
    /// afford to be far more patient than a live call.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        if (_unavailable || _tooSlow)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));

        try
        {
            // A real brief, not an empty prompt: an empty one would load the
            // model and measure nothing, and the measurement is half the
            // point. Short and fixed so the timing means the same thing on
            // every machine. Through the same queue as a live line, so a
            // warm-up started while a race is being written waits rather
            // than running beside it.
            //
            // The clock is started inside, once the turn has come and the
            // weights are in memory (14 Oct 2026). Started out here it timed
            // the cold load of 440 MB off disk and any line queued ahead as
            // well, so a PC that writes in two seconds but loads in twenty
            // was judged "too slow" and had the built-in model switched off
            // for the night - for a cost a live line never pays.
            var clock = new Stopwatch();

            var line = await RunGuardedAsync(
                "You are a race announcer. Answer in one short sentence.",
                "Say that the next race is about to start.",
                maxTokens: 40,
                temperature: 0.7,
                timeoutCts.Token,
                clock).ConfigureAwait(false);

            clock.Stop();
            _measured = clock.Elapsed;

            if (string.IsNullOrWhiteSpace(line.Text))
            {
                StatusMessage?.Invoke($"[llm] Warmed up in {_measured.TotalSeconds:0.#}s but wrote nothing - the model may not be the one this expects.");
                return;
            }

            if (_measured > Timeout)
            {
                _tooSlow = true;

                StatusMessage?.Invoke(
                    $"[llm] This PC took {_measured.TotalSeconds:0.#}s to write one line, past the {Timeout.TotalSeconds:0.#}s a live call is given. " +
                    "The built-in model is switched off rather than making every segment wait and then say nothing. " +
                    "Give it longer on the AI Commentary tab if you would rather it tried.");

                return;
            }

            StatusMessage?.Invoke($"[llm] Ready - wrote a test line in {_measured.TotalSeconds:0.#}s.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            StatusMessage?.Invoke("[llm] Warm-up gave up after 3 minutes - this PC is very likely too slow to use the built-in model.");
        }
        catch (OperationCanceledException)
        {
            // Called off deliberately - settings changed, or the app is
            // closing. Nothing to report.
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[llm] Warm-up failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The Test button on the Commentary page. Null when it did not work.</summary>
    public async Task<string?> TestAsync(CancellationToken ct = default) =>
        await GenerateAsync(
            "You are a test. Reply with exactly: OK",
            "Reply with exactly: OK",
            maxTokens: 10,
            ct: ct).ConfigureAwait(false);

    /// <summary>
    /// Drops this client's executor. The WEIGHTS are not released here, and
    /// that is deliberate - they belong to the process, not to this object
    /// (see the note on the static fields). Disposing them here is what made
    /// a rebuilt client reload 440 MB.
    ///
    /// The semaphore is left undisposed on purpose too. It costs nothing,
    /// and a disposed one turns a harmless use-after-dispose - which a
    /// settings page that rebuilds freely can easily produce - into an
    /// exception on a race night.
    /// </summary>
    public void Dispose() => _parameters = null;
}
