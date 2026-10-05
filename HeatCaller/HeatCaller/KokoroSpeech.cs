using KokoroSharp;
using KokoroSharp.Processing;
using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// One Kokoro voice, as the Voice Settings list shows it. Name is what is
/// saved and what Kokoro picks a voice by ("am_michael"); Display is what is
/// read: "Michael (American, male)".
/// </summary>
internal sealed record KokoroVoiceChoice(string Name, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// The Kokoro voice: a free, natural-sounding voice that runs on this PC
/// (Kokoro 82M, through KokoroSharp and ONNX Runtime on the CPU). No account,
/// no key, no internet once it is downloaded - and nothing is ever billed, so
/// it costs a club nothing per race however much the announcer talks.
///
/// THE MODEL is not shipped with the app: it is about 310 MB, and a club on
/// ElevenLabs or the HeatCaller voice would never use it. Voice Settings has
/// a Download button that fetches it once into LocalRoot\Kokoro - the same
/// machine-only folder as the saved-voice library, so it never goes into a
/// backup. Until it is there this voice is simply unavailable, and the
/// Windows voice says what it would have.
///
/// The voices themselves (small .npy files) come with the KokoroSharp package
/// and are copied next to the exe on build, under voices\.
///
/// ONE RENDER AT A TIME through a gate of its own, like the Windows voice -
/// one CPU-bound model is no faster for being asked twice at once. Lines are
/// kept in memory once rendered (see LineCache), so a fixed line - a flag
/// call, "green flag, green flag" - is rendered once a session and then
/// plays at once.
///
/// Nothing rendered here goes into the saved-voice library: that holds
/// ElevenLabs and HeatCaller takes only, keyed by their settings.
/// </summary>
internal static class KokoroSpeech
{
    /// <summary>The voice used when none is picked, or the one picked isn't on this PC. An American male announcer.</summary>
    internal const string DefaultVoice = "am_michael";

    /// <summary>Roughly how big the download is, for the button and the question before it.</summary>
    internal const string DownloadSizeText = "about 310 MB";

    /// <summary>
    /// The full-precision Kokoro v1.0 model, from the KokoroSharp project's
    /// own releases - the exact file KokoroSharp's own loader downloads. Its
    /// loader isn't used because it saves into the working directory, which
    /// is wherever the app happened to be started from.
    /// </summary>
    private const string ModelUrl = "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx";

    /// <summary>Anything smaller than this is a broken download, not the model.</summary>
    private const long SmallestRealModel = 50L * 1024 * 1024;

    /// <summary>The longest one render may take. Kokoro renders a race line in well under a second on an ordinary laptop; this is a line that has stuck.</summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The longest a line waits for the one before it - longer than RenderTimeout, so a line is never failed for waiting behind one still allowed to finish.</summary>
    private static readonly TimeSpan GateWait = TimeSpan.FromSeconds(40);

    /// <summary>The longest a line waits for the model to load. The first load reads 310 MB off the disk.</summary>
    private static readonly TimeSpan LoadWait = TimeSpan.FromSeconds(60);

    /// <summary>How long a model that failed to load is left before it is tried again.</summary>
    private static readonly TimeSpan RetryLoadAfter = TimeSpan.FromMinutes(1);

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly object EngineLock = new();
    private static Task<KokoroWavSynthesizer>? _loading;
    private static string? _loadFailure;
    private static DateTime _loadFailedAtUtc;

    private static readonly object ReportLock = new();
    private static readonly HashSet<string> MissingVoicesReported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where Kokoro's problems are reported, said once each. Set by the announcer.</summary>
    internal static Action<string>? Log { get; set; }

    /// <summary>
    /// Tests only: stands in for the loaded model - finding a voice by name,
    /// and saying a line in it at a speed - since the 310 MB model can't be
    /// loaded in a test. Always null in the app.
    /// </summary>
    internal static (Func<string, object> FindVoice, Func<string, object, float, Task<byte[]>> Say)? ModelForTests { get; set; }

    /// <summary>The folder the model is kept in - this PC only, never backed up.</summary>
    internal static string Folder => Path.Combine(AppPaths.LocalRoot, "Kokoro");

    internal static string ModelPath => Path.Combine(Folder, "kokoro.onnx");

    /// <summary>The voices folder KokoroSharp's package copies next to the exe.</summary>
    private static string VoicesFolder => Path.Combine(AppContext.BaseDirectory, "voices");

    internal static bool IsModelDownloaded
    {
        get
        {
            try
            {
                var file = new FileInfo(ModelPath);
                return file.Exists && file.Length >= SmallestRealModel;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Why the Kokoro voice can't speak on this PC - lower case, to go inside
    /// a sentence - or null when it can.
    /// </summary>
    internal static string? UnavailableReason
    {
        get
        {
            if (!IsModelDownloaded)
            {
                return "the Kokoro voice isn't downloaded on this PC yet (Voice Settings has a Download button)";
            }

            lock (EngineLock)
            {
                return _loadFailure is not null && DateTime.UtcNow - _loadFailedAtUtc < RetryLoadAfter
                    ? _loadFailure
                    : null;
            }
        }
    }

    internal static bool IsAvailable => UnavailableReason is null;

    /// <summary>
    /// Why the model wouldn't load, lower case, or null - kept until a load
    /// works or a new model is downloaded. UnavailableReason lets it go after
    /// RetryLoadAfter, so a line can try again; what the page and the Mixer
    /// show reads this instead. They used to read that one, and went back to
    /// "ready" a minute after each failure while the Windows voice said
    /// every line.
    /// </summary>
    internal static string? LoadFailure
    {
        get
        {
            lock (EngineLock)
            {
                return _loadFailure;
            }
        }
    }

    /// <summary>
    /// Whether Voice Settings offers the download: there's no model yet, or
    /// the one there wouldn't load - its reason says to download it again,
    /// and the button used to be hidden whenever a file of the right size
    /// was there.
    /// </summary>
    internal static bool ShouldOfferDownload => !IsModelDownloaded || LoadFailure is not null;

    /// <summary>Tests only: as if the model had failed to load at <paramref name="atUtc"/>, for <paramref name="reason"/>.</summary>
    internal static void RecordLoadFailureForTests(string? reason, DateTime atUtc)
    {
        lock (EngineLock)
        {
            _loading = null;
            _loadFailure = reason;
            _loadFailedAtUtc = atUtc;
        }
    }

    /// <summary>
    /// Loads the model in the background, so the first line of the night
    /// doesn't wait for 310 MB to be read. Does nothing when it isn't
    /// downloaded.
    /// </summary>
    internal static void WarmUp()
    {
        if (!IsModelDownloaded)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await EngineAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Said by EngineAsync, and tried again on the next line.
            }
        });
    }

    // --- The voices ---------------------------------------------------------

    /// <summary>
    /// The English voices, read from the names of the files KokoroSharp ships -
    /// nothing is loaded to list them. American first, then British; women,
    /// then men. Falls back to the voices Kokoro v1.0 is known to have if the
    /// folder can't be read, so the list is never empty.
    /// </summary>
    internal static IReadOnlyList<KokoroVoiceChoice> InstalledVoices()
    {
        IEnumerable<string> names;

        try
        {
            names = Directory.Exists(VoicesFolder)
                ? Directory.EnumerateFiles(VoicesFolder, "*.npy", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).OfType<string>()
                : KnownEnglishVoices;
        }
        catch (Exception)
        {
            names = KnownEnglishVoices;
        }

        var choices = names
            .Where(IsEnglishVoice)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n[0])
            .ThenBy(n => n[1])
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new KokoroVoiceChoice(n, Describe(n)))
            .ToList();

        return choices.Count > 0
            ? choices
            : KnownEnglishVoices.Select(n => new KokoroVoiceChoice(n, Describe(n))).ToList();
    }

    private static readonly string[] KnownEnglishVoices =
    {
        "af_heart", "af_bella", "af_nicole", "af_sarah", "af_sky",
        "am_adam", "am_echo", "am_eric", "am_fenrir", "am_liam", "am_michael", "am_onyx", "am_puck",
        "bf_alice", "bf_emma", "bf_isabella", "bf_lily",
        "bm_daniel", "bm_fable", "bm_george", "bm_lewis",
    };

    /// <summary>"am_michael" and the like: American or British English, female or male.</summary>
    private static bool IsEnglishVoice(string name) =>
        name.Length > 3
        && name[2] == '_'
        && (name[0] == 'a' || name[0] == 'b')
        && (name[1] == 'f' || name[1] == 'm');

    /// <summary>"am_michael" → "Michael (American, male)".</summary>
    internal static string Describe(string name)
    {
        if (!IsEnglishVoice(name))
        {
            return name;
        }

        var given = name[3..];
        given = char.ToUpperInvariant(given[0]) + given[1..];

        return $"{given} ({(name[0] == 'a' ? "American" : "British")}, {(name[1] == 'f' ? "female" : "male")})";
    }

    // --- Speaking -----------------------------------------------------------

    /// <summary>
    /// Renders a line as a WAV, in the Kokoro voice the profile picks, at its
    /// speed. Audio tags are taken out first - Kokoro would read "[excited]"
    /// out as words.
    /// </summary>
    /// <exception cref="InvalidOperationException">With a plain reason, whenever it can't speak the line.</exception>
    internal static async Task<byte[]> RenderAsync(VoiceProfile profile, string text, CancellationToken cancel = default)
    {
        var spoken = VoiceProfile.StripAudioTags(text ?? string.Empty);

        if (string.IsNullOrWhiteSpace(spoken))
        {
            throw new InvalidOperationException("There were no words left to say once the audio tags were taken out.");
        }

        if (UnavailableReason is { } reason)
        {
            throw new InvalidOperationException($"The Kokoro voice can't speak - {reason}.");
        }

        var voiceName = string.IsNullOrWhiteSpace(profile.KokoroVoiceName) ? DefaultVoice : profile.KokoroVoiceName.Trim();
        var speed = (float)Math.Clamp(profile.Speed, 0.7, 1.2);
        var key = $"{voiceName}|{speed:0.###}|{spoken}";

        if (LineCache.TryGet(key) is { } kept)
        {
            return kept;
        }

        // The two steps that need the model: finding the voice, and saying
        // the line in it. A test stands in for both (ModelForTests).
        Func<string, object> findVoice;
        Func<string, object, Task<byte[]>> say;

        if (ModelForTests is { } standIn)
        {
            findVoice = standIn.FindVoice;
            say = (line, voice) => standIn.Say(line, voice, speed);
        }
        else
        {
            KokoroWavSynthesizer engine;
            try
            {
                engine = await EngineAsync().WaitAsync(LoadWait, cancel).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException($"The Kokoro voice was still loading after {LoadWait.TotalSeconds:0} s.");
            }

            // KokoroSharp's own default for whole-line synthesis, with the
            // speed set: a first segment as long as the model takes, since
            // the whole line is wanted before any of it plays.
            var config = new KokoroTTSPipelineConfig(new DefaultSegmentationConfig { MaxFirstSegmentLength = 510 })
            {
                Speed = speed,
            };

            findVoice = name => VoiceFor(name);
            say = (line, voice) => engine.SynthesizeAsync(line, (KokoroSharp.Core.KokoroVoice)voice, config);
        }

        if (!await Gate.WaitAsync(GateWait, cancel).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"The Kokoro voice was still busy with the line before this one after {GateWait.TotalSeconds:0} s.");
        }

        byte[] pcm;
        try
        {
            // On the pool, still inside the gate. It used to start right
            // here, on the caller's thread - the UI thread, for the voice
            // queue and the Test buttons - and with the model loaded and the
            // gate free nothing above leaves that thread. The first line of
            // the night then loaded every voice file and KokoroSharp's
            // pronunciation dictionary before its first await, and the window
            // froze meanwhile. Finding the voice has no timeout: a render given
            // up on releases the gate, and the voice list must not still be
            // loading then (see VoiceFor).
            var voice = await Task.Run(() => findVoice(voiceName)).ConfigureAwait(false);
            pcm = await Task.Run(() => say(spoken, voice)).WaitAsync(RenderTimeout, cancel).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"The Kokoro voice took longer than {RenderTimeout.TotalSeconds:0} s to say that line.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            throw new InvalidOperationException($"The Kokoro voice couldn't say that line - {ex.Message}", ex);
        }
        finally
        {
            Gate.Release();
        }

        // A tenth of a second at 24 kHz, 16-bit mono. Anything shorter isn't a line.
        if (pcm.Length < 4800)
        {
            throw new InvalidOperationException("The Kokoro voice produced no usable audio for that line.");
        }

        var wav = EncodeWav(pcm, KokoroPlayback.waveFormat);
        LineCache.Put(key, wav);
        return LineCache.Copy(wav);
    }

    /// <summary>
    /// The voice by name, or the default one when this PC doesn't have it - a
    /// settings file from another laptop, or a voice a later KokoroSharp
    /// dropped. Said once per voice. Called inside the gate: KokoroSharp's
    /// voice list is not safe to load from two threads at once.
    /// </summary>
    private static KokoroSharp.Core.KokoroVoice VoiceFor(string name)
    {
        try
        {
            return KokoroVoiceManager.GetVoice(name);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException)
        {
            if (ex is DirectoryNotFoundException)
            {
                throw new InvalidOperationException("Kokoro's voices folder is missing from beside HeatCaller.exe - reinstall HeatCaller.");
            }

            if (string.Equals(name, DefaultVoice, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Kokoro's own default voice ({DefaultVoice}) isn't on this PC.");
            }

            lock (ReportLock)
            {
                if (MissingVoicesReported.Add(name))
                {
                    Log?.Invoke($"[voice] The Kokoro voice \"{name}\" isn't on this PC - {Describe(DefaultVoice)} is speaking instead.");
                }
            }

            return KokoroVoiceManager.GetVoice(DefaultVoice);
        }
    }

    /// <summary>
    /// The model, loaded once and kept. A load that failed is said once, and
    /// tried again after RetryLoadAfter rather than on every line.
    /// </summary>
    private static Task<KokoroWavSynthesizer> EngineAsync()
    {
        lock (EngineLock)
        {
            if (_loading is { IsFaulted: false, IsCanceled: false } loading)
            {
                return loading;
            }

            if (_loading is not null && DateTime.UtcNow - _loadFailedAtUtc < RetryLoadAfter)
            {
                return _loading;
            }

            _loading = Task.Run(() => Load());
            return _loading;
        }

        static KokoroWavSynthesizer Load()
        {
            try
            {
                var engine = new KokoroWavSynthesizer(ModelPath);

                lock (EngineLock)
                {
                    _loadFailure = null;
                }

                return engine;
            }
            catch (Exception ex)
            {
                var reason = $"the Kokoro model wouldn't load ({ex.GetType().Name}: {ex.Message}) - downloading it again on Voice Settings usually fixes this";

                lock (EngineLock)
                {
                    _loadFailure = reason;
                    _loadFailedAtUtc = DateTime.UtcNow;
                }

                Log?.Invoke($"[voice] {char.ToUpperInvariant(reason[0])}{reason[1..]}.");
                throw new InvalidOperationException($"The Kokoro voice can't speak - {reason}.", ex);
            }
        }
    }

    private static byte[] EncodeWav(byte[] pcm, WaveFormat format)
    {
        // Closing the writer is what fills in the header's lengths; ToArray
        // still works on the closed stream.
        var memory = new MemoryStream();

        using (var writer = new WaveFileWriter(memory, format))
        {
            writer.Write(pcm, 0, pcm.Length);
        }

        return memory.ToArray();
    }

    // --- Downloading the model ----------------------------------------------

    /// <summary>What the model is fetched with. Set by a test, which can't fetch 310 MB.</summary>
    internal static HttpClient Http { get; set; } = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly object DownloadLock = new();
    private static Task? _download;
    private static CancellationTokenSource? _downloadCancel;

    /// <summary>How far the download has got, 0-1, in whole percent steps - raised on the thread doing it.</summary>
    internal static event Action<double>? DownloadProgressed;

    /// <summary>The download running now, to follow, or null when there isn't one.</summary>
    internal static Task? RunningDownload
    {
        get
        {
            lock (DownloadLock)
            {
                return _download is { IsCompleted: false } running ? running : null;
            }
        }
    }

    internal static bool IsDownloading => RunningDownload is not null;

    /// <summary>
    /// Starts fetching the model into <see cref="Folder"/>, or hands back the
    /// download already running. Held here, not by the Voice Settings page:
    /// that page is rebuilt when a soundscape, venue or Lap times only is
    /// applied, and the download it held went on unseen and uncancellable,
    /// with a second Download failing on the .part file it still had open.
    /// </summary>
    internal static Task DownloadModelAsync()
    {
        lock (DownloadLock)
        {
            if (_download is { IsCompleted: false } running)
            {
                return running;
            }

            // Not disposed after: it holds nothing that needs it, and a
            // Cancel pressed just as the download ends would find it disposed.
            var cancel = new CancellationTokenSource();
            _downloadCancel = cancel;
            _download = Task.Run(() => FetchModelAsync(cancel.Token));
            return _download;
        }
    }

    /// <summary>Stops the download running now, if there is one; its task ends canceled.</summary>
    internal static void CancelDownload()
    {
        CancellationTokenSource? cancel;
        lock (DownloadLock)
        {
            cancel = _download is { IsCompleted: false } ? _downloadCancel : null;
        }

        // Outside the lock: canceling runs what was waiting on it.
        cancel?.Cancel();
    }

    /// <summary>
    /// Deletes the .part file of a download HeatCaller was closed part way
    /// through - up to 310 MB, in a folder nobody looks in. At startup.
    /// </summary>
    internal static void DeleteLeftoverDownload()
    {
        if (!IsDownloading)
        {
            TryDelete(ModelPath + ".part");
        }
    }

    private static async Task FetchModelAsync(CancellationToken cancel)
    {
        using var response = await Http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        await SaveModelAsync(source, response.Content.Headers.ContentLength, ToListeners, cancel).ConfigureAwait(false);
    }

    private static readonly IProgress<double> ToListeners = new ListenerProgress();

    private sealed class ListenerProgress : IProgress<double>
    {
        public void Report(double value) => DownloadProgressed?.Invoke(value);
    }

    /// <summary>
    /// Saves the model from <paramref name="source"/>, reporting 0-1 as it
    /// goes. Written to a .part file and only renamed once complete, so a
    /// download cut short never leaves a file that looks like the model.
    /// </summary>
    internal static async Task SaveModelAsync(Stream source, long? length, IProgress<double>? progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(Folder);
        var part = ModelPath + ".part";

        try
        {
            var total = length ?? 325_000_000L;
            var buffer = new byte[81920];
            long done = 0;
            var lastReported = -1;

            await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
            {
                int read;
                while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    done += read;

                    // Whole percent steps only - the page doesn't need
                    // thousands of updates.
                    var percent = (int)(100 * done / Math.Max(1, total));
                    if (percent != lastReported)
                    {
                        lastReported = percent;
                        progress?.Report(Math.Clamp(done / (double)Math.Max(1, total), 0, 1));
                    }
                }
            }

            if (done < SmallestRealModel)
            {
                throw new InvalidOperationException($"The download stopped after {done / (1024 * 1024)} MB - that isn't the whole model. Try again.");
            }

            // A line trying the old model again has it open while it loads,
            // which would fail the move - now that a model that won't load
            // can be downloaded again over it.
            Task? loading;
            lock (EngineLock)
            {
                loading = _loading is { IsCompleted: false } ? _loading : null;
            }

            if (loading is not null)
            {
                try
                {
                    await loading.WaitAsync(LoadWait, cancel).ConfigureAwait(false);
                }
                catch (Exception) when (!cancel.IsCancellationRequested)
                {
                    // It failed, or is still going after LoadWait: the move
                    // is tried anyway.
                }
            }

            File.Move(part, ModelPath, overwrite: true);
        }
        catch (Exception)
        {
            // It used to delete the .part only when a download ended short,
            // so one canceled, cut off or stopped by a full disk left up to
            // 310 MB behind - on a disk that might then have no room for the
            // club's own saves, after saying "nothing was kept".
            TryDelete(part);
            throw;
        }

        lock (EngineLock)
        {
            // A model that failed to load before is a new file now.
            _loadFailure = null;
            _loading = null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // A .part file left behind is overwritten by the next try.
        }
    }

    /// <summary>
    /// Rendered lines, newest kept, for this session only. A race says the
    /// same fixed lines again and again - flags, countdowns, "last lap" - and
    /// the same driver names all night, so keeping a few hundred means most
    /// lines after the first race play without rendering at all.
    /// </summary>
    private static class LineCache
    {
        private const int Capacity = 400;
        private static readonly object Lock = new();
        private static readonly Dictionary<string, LinkedListNode<(string Key, byte[] Wav)>> Index = new(StringComparer.Ordinal);
        private static readonly LinkedList<(string Key, byte[] Wav)> Order = new();

        public static byte[]? TryGet(string key)
        {
            lock (Lock)
            {
                if (!Index.TryGetValue(key, out var node))
                {
                    return null;
                }

                Order.Remove(node);
                Order.AddFirst(node);
                return Copy(node.Value.Wav);
            }
        }

        public static void Put(string key, byte[] wav)
        {
            lock (Lock)
            {
                if (Index.TryGetValue(key, out var existing))
                {
                    Order.Remove(existing);
                }

                Index[key] = Order.AddFirst((key, wav));

                while (Order.Count > Capacity && Order.Last is { } oldest)
                {
                    Order.RemoveLast();
                    Index.Remove(oldest.Value.Key);
                }
            }
        }

        /// <summary>Callers get their own copy: a WAV handed out is sometimes repaired in place on its way to the speaker.</summary>
        public static byte[] Copy(byte[] wav) => (byte[])wav.Clone();
    }
}
