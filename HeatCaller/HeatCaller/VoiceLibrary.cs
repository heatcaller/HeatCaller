using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// ElevenLabs renders kept on disk, so a line is paid for once rather than
/// every time it is said - including across restarts, which is when the old
/// in-memory copies were all thrown away and every flag line was bought
/// again the next time a race staged.
///
/// KEYED BY EXACTLY WHAT WAS ASKED FOR. The key is a hash of the words and
/// every setting ElevenLabs is sent that changes the sound: voice, model,
/// stability, similarity, style, speaker boost, speed, text normalisation,
/// language and output format. Change any of them - a new voice, a tweak to
/// stability, one word of a line - and the key is different, so that line is
/// rendered afresh the next time it is needed and everything unchanged keeps
/// playing from here. That is the whole of "only update what changes": there
/// is no list of lines to keep in step, because a line that changed simply
/// stops matching. The API key is deliberately NOT part of it - the audio
/// does not depend on whose account paid for it.
///
/// WHAT IS IN HERE. Everything the app has said, the first time it said it.
/// A line built from live numbers - a lap time, a running order - is saved
/// too, but it rarely comes round again word for word, so those mostly age
/// out under the size cap while the lines that do repeat (flags, a driver's
/// release call, "takes the lead") are touched each time they play and stay.
///
/// WHERE. %LocalAppData%, not beside the club's data in %AppData%: this is a
/// cache - every file in it can be made again - so it has no place in a
/// backup, and on a PC with a roaming profile it must not roam.
///
/// Every method is safe from any thread, and none of them throws: a library
/// that cannot be read or written costs money, not announcements.
/// </summary>
internal sealed class VoiceLibrary
{
    /// <summary>
    /// Where the library lives. Worked out when asked for, not stored:
    /// static initialisers run in the order they are written, and a stored
    /// value declared below <see cref="Shared"/> would still be null when
    /// Shared was built from it - every save and every lookup would then
    /// quietly fail against a folder of null.
    /// </summary>
    internal static string DefaultFolder => Path.Combine(
        AppPaths.LocalRoot,
        "VoiceLibrary");

    /// <summary>The library every part of the app shares.</summary>
    internal static VoiceLibrary Shared { get; } = new(DefaultFolder);

    /// <summary>
    /// Part of every key. Changed only if what goes INTO a key ever changes,
    /// so old entries stop matching rather than being played for a request
    /// they no longer describe. Kept at its original value through the
    /// rename to HeatCaller: changing it would throw away every line a club
    /// has already paid ElevenLabs to render.
    /// </summary>
    private const string KeyVersion = LegacyNames.VoiceKeyVersion;

    /// <summary>
    /// The size the library is allowed to reach before the least recently
    /// used lines are cleared out. About ten thousand ordinary lines.
    /// </summary>
    internal const long MaxBytes = 500L * 1024 * 1024;

    /// <summary>Where a clear-out stops, so it is not run again a few lines later.</summary>
    internal const long PruneToBytes = 400L * 1024 * 1024;

    private const string Mp3Extension = ".mp3";
    private const string WavExtension = ".wav";
    private const string InfoExtension = ".json";
    private const string TempExtension = ".tmp";

    /// <summary>A save counts toward the next size check; this many saves and it runs.</summary>
    private const int SavesBetweenPrunes = 50;

    private readonly string _folder;
    private readonly object _pruneLock = new();
    private int _pruneStarted;
    private int _savesSincePrune;
    private int _saveFailureReported;

    internal VoiceLibrary(string folder) => _folder = folder;

    internal string Folder => _folder;

    /// <summary>
    /// Raised after a line is saved or forgotten, on whatever thread did it -
    /// a handler that touches controls must marshal first.
    /// </summary>
    internal event Action? Changed;

    /// <summary>Where the library reports what went wrong. The announcer points this at its console.</summary>
    internal Action<string>? Log { get; set; }

    /// <summary>What a saved file holds, decided from its first bytes rather than trusted from its name.</summary>
    internal enum SavedAudioKind
    {
        Unknown,
        Mp3,
        Wav,
    }

    /// <summary>A saved line, for the Voice Settings page to show and forget.</summary>
    internal sealed record SavedLine(string Key, string Text, string VoiceName, DateTime LastUsedUtc, long Bytes);

    internal readonly record struct LibraryStats(int Lines, long Bytes);

    /// <summary>What is written beside each line, so it can be listed by its words.</summary>
    private sealed class SavedLineInfo
    {
        public string Text { get; set; } = string.Empty;
        public string VoiceName { get; set; } = string.Empty;
        public string VoiceId { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public DateTime SavedUtc { get; set; }
    }

    /// <summary>
    /// The app's settings, read for the three voice-relay boxes on Voice
    /// Settings - which relay a HeatCaller line was rendered on is part of
    /// its key (see <see cref="KeyFor"/>). Set by the Voice Settings page as
    /// it is built, which is at startup. Null - tests, and the first moments
    /// of startup - keys every line as the club's own relay's, which is
    /// what a blank set of boxes means anyway.
    /// </summary>
    internal static AppSettings? VoiceRelaySettings { get; set; }

    /// <summary>
    /// The voice relay the HeatCaller voice is pointed at when it is NOT the
    /// club's own - its tidied address and the club id used on it, the same
    /// way HeatCallerVoiceClient works them out - or empty while the voice
    /// relay boxes are blank. Read on every key: the boxes can change while
    /// the app runs.
    /// </summary>
    internal static string HeatCallerRelayIdentity()
    {
        if (VoiceRelaySettings is not { } settings)
        {
            return string.Empty;
        }

        var root = RelayPublisher.NormaliseBaseUrl(settings.VoiceRelayBaseUrl);

        if (root.Length == 0)
        {
            return string.Empty;
        }

        // The club id falls back to the Club Website page's when its box is left
        // blank, exactly as the client does - it is part of which club's
        // take this is, whichever box it came from.
        var club = !string.IsNullOrWhiteSpace(settings.VoiceRelayClub)
            ? settings.VoiceRelayClub.Trim().ToLowerInvariant()
            : (settings.RelayClub ?? string.Empty).Trim().ToLowerInvariant();

        // The club's own relay and club typed into the voice boxes as well is
        // still the club's own relay: its takes are the ones already saved
        // under the plain key, and keying them apart only paid for every
        // line a second time. Compared the way HeatCallerVoiceClient decides
        // "the same relay".
        var ownRoot = RelayPublisher.NormaliseBaseUrl(settings.RelayBaseUrl);
        var ownClub = (settings.RelayClub ?? string.Empty).Trim().ToLowerInvariant();

        if (string.Equals(root, ownRoot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(club, ownClub, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return root.ToLowerInvariant() + "\n" + club;
    }

    /// <summary>
    /// The key for one request: a hash of every field ElevenLabs is sent
    /// that shapes the audio, exactly as ElevenLabsClient sends it.
    ///
    /// Each value is written with its length in front, so no value can run
    /// into the next and two different requests can never write the same
    /// text to be hashed. Numbers go in round-trip form, in the invariant
    /// culture, so 0.5 is always "0.5" whatever the PC's regional settings.
    /// </summary>
    internal static string KeyFor(VoiceProfile profile, string text)
    {
        var sb = new StringBuilder(256 + text.Length);

        void Add(string name, string? value)
        {
            value ??= string.Empty;
            sb.Append(name).Append(':').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
        }

        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        Add("version", KeyVersion);

        // The HeatCaller voice is keyed by what the club actually chose and
        // the relay's fingerprint for it - the voice id, the model and the
        // settings behind it are the relay's, and this laptop is never told
        // them. Kept in its own branch so a club using its own ElevenLabs
        // key keeps every line it has already paid for: the fields below
        // are exactly the ones this has always hashed.
        if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            Add("engine", LegacyNames.VoiceKeyEngine);
            Add("gcvoice", profile.HeatCallerVoice);
            Add("gcrev", profile.HeatCallerRev);

            // Which relay spoke it, when that is not the club's own. A test
            // relay offers voices under the same short ids and revisions as
            // the real one, but they can be quite different voices - a
            // relay started on somebody's laptop with their own ElevenLabs
            // account, the cheapest model - and without this, a line
            // rendered there was saved under exactly the key the production
            // relay's take would have, and went on playing at the club
            // night after the boxes were emptied. Only added while a voice
            // relay of its own is set, so the key for the club's own relay
            // - every line already saved on every laptop - is exactly what
            // it has always been.
            var relay = HeatCallerRelayIdentity();

            if (relay.Length > 0)
            {
                Add("gcrelay", relay);
            }

            Add("text", text);

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        }

        // The same blank-means-default rules as ElevenLabsClient: a blank
        // normalisation is sent as "auto", and a blank language or output
        // format is not sent at all.
        Add("voice", profile.VoiceId);
        Add("model", profile.ModelId);
        Add("normalize", string.IsNullOrWhiteSpace(profile.TextNormalization) ? "auto" : profile.TextNormalization);
        Add("language", string.IsNullOrWhiteSpace(profile.LanguageCode) ? string.Empty : profile.LanguageCode);
        Add("format", string.IsNullOrWhiteSpace(profile.OutputFormat) ? string.Empty : profile.OutputFormat);
        Add("stability", Number(profile.Stability));
        Add("similarity", Number(profile.SimilarityBoost));
        Add("style", Number(profile.Style));
        Add("boost", profile.SpeakerBoost ? "1" : "0");
        Add("speed", Number(profile.Speed));
        Add("text", text);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// What some audio bytes are. Anything but MP3 or WAV is Unknown and is
    /// never saved: a raw PCM or Opus output format is something this app
    /// cannot play back later anyway.
    /// </summary>
    internal static SavedAudioKind SniffAudio(byte[]? audio)
    {
        // Far below the smallest real line; a file this short is damaged.
        if (audio is null || audio.Length < 256)
        {
            return SavedAudioKind.Unknown;
        }

        if (audio[0] == 'R' && audio[1] == 'I' && audio[2] == 'F' && audio[3] == 'F'
            && audio[8] == 'W' && audio[9] == 'A' && audio[10] == 'V' && audio[11] == 'E')
        {
            return SavedAudioKind.Wav;
        }

        // An ID3 tag, or an MPEG audio frame's eleven sync bits.
        if ((audio[0] == 'I' && audio[1] == 'D' && audio[2] == '3')
            || (audio[0] == 0xFF && (audio[1] & 0xE0) == 0xE0))
        {
            return SavedAudioKind.Mp3;
        }

        return SavedAudioKind.Unknown;
    }

    /// <summary>
    /// The saved audio for this request, or null when there is none - or
    /// none that can be trusted, in which case it is cleared so the next
    /// render replaces it.
    /// </summary>
    internal async Task<byte[]?> TryLoadAsync(VoiceProfile profile, string text)
    {
        if (!CanKey(profile, text))
        {
            return null;
        }

        var key = KeyFor(profile, text);
        var path = FindAudio(key);

        if (path is null)
        {
            return null;
        }

        byte[] audio;
        try
        {
            // Off the caller's thread from here: the voice queue runs on the
            // UI thread, and a slow disk must not be felt there.
            audio = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use by a save of the same line, or gone since the check.
            // Either way a render is the safe answer.
            return null;
        }

        var expected = path.EndsWith(WavExtension, StringComparison.OrdinalIgnoreCase)
            ? SavedAudioKind.Wav
            : SavedAudioKind.Mp3;

        if (SniffAudio(audio) != expected)
        {
            Remove(key);
            Log?.Invoke($"[voice library] A saved line was damaged and has been cleared - it will be rendered again: \"{Shorten(text)}\"");
            return null;
        }

        // Touched on use, so the size cap clears the lines nobody hears
        // rather than the ones that come round every week.
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only affects which lines a clear-out keeps.
        }

        StartPruneOnce();
        return audio;
    }

    /// <summary>True when this request is already saved. A file check, nothing read.</summary>
    internal bool Contains(VoiceProfile profile, string text) =>
        CanKey(profile, text) && FindAudio(KeyFor(profile, text)) is not null;

    /// <summary>
    /// Keeps a render, in the background. Called straight after ElevenLabs
    /// answers, so it must never hold up the line that is about to play.
    /// </summary>
    internal void Save(VoiceProfile profile, string text, byte[] audio)
    {
        if (!CanKey(profile, text))
        {
            return;
        }

        var kind = SniffAudio(audio);

        if (kind == SavedAudioKind.Unknown)
        {
            return;
        }

        var key = KeyFor(profile, text);
        var heatCaller = profile.EffectiveEngine == VoiceEngine.HeatCaller;

        var info = new SavedLineInfo
        {
            Text = text,

            // Display only, for the Saved Voice Lines list. A HeatCaller
            // line is listed by the voice the club picked, not by an
            // ElevenLabs voice id it has never had.
            VoiceName = heatCaller
                ? (string.IsNullOrWhiteSpace(profile.HeatCallerVoiceName) ? profile.HeatCallerVoice : profile.HeatCallerVoiceName)
                : string.IsNullOrWhiteSpace(profile.VoiceName) ? profile.VoiceId : profile.VoiceName,
            VoiceId = heatCaller ? profile.HeatCallerVoice : profile.VoiceId,
            ModelId = heatCaller ? "heatcaller" : profile.ModelId,
            SavedUtc = DateTime.UtcNow,
        };

        _ = Task.Run(() => SaveCore(key, kind, audio, info));
    }

    private void SaveCore(string key, SavedAudioKind kind, byte[] audio, SavedLineInfo info)
    {
        try
        {
            Directory.CreateDirectory(_folder);

            if (kind == SavedAudioKind.Wav && File.Exists(AudioPath(key, Mp3Extension)))
            {
                // A clip cut out of a batch never replaces a line rendered
                // on its own - see PrimeReleaseLines, which only batches the
                // lines that were missing.
                return;
            }

            WriteAtomically(AudioPath(key, kind == SavedAudioKind.Wav ? WavExtension : Mp3Extension), audio);

            // One line, one file: a WAV cut left from before an MP3 of the
            // same line arrived would otherwise sit there unused.
            if (kind == SavedAudioKind.Mp3)
            {
                TryDelete(AudioPath(key, WavExtension));
            }

            WriteAtomically(InfoPath(key), JsonSerializer.SerializeToUtf8Bytes(info));
        }
        catch (Exception ex)
        {
            // The same line being read, or saved by another render, at this
            // exact moment holds the file open and the swap is refused. The
            // line is already here, so there is nothing to report.
            if ((ex is IOException or UnauthorizedAccessException) && FindAudio(key) is not null)
            {
                return;
            }

            // Said once a session. The line still played; it will just be
            // paid for again next time.
            if (Interlocked.Exchange(ref _saveFailureReported, 1) == 0)
            {
                Log?.Invoke($"[voice library] Couldn't save a line to {_folder} ({ex.Message}). Lines still play - they are just rendered again each time.");
            }

            return;
        }

        Changed?.Invoke();

        if (Interlocked.Increment(ref _savesSincePrune) >= SavesBetweenPrunes)
        {
            Interlocked.Exchange(ref _savesSincePrune, 0);
            Prune();
        }
    }

    /// <summary>
    /// Forgets one line by its key, so the next time it is wanted it is
    /// rendered afresh. With <paramref name="holdIn"/> (a folder from
    /// UndoHoldingFolder.NewFolder(local: true)) its files are moved there
    /// instead of deleted, so <see cref="PutBackHeld"/> can undo it.
    /// </summary>
    internal bool Forget(string key, string? holdIn = null)
    {
        if (!IsKey(key))
        {
            return false;
        }

        var removed = holdIn is null ? Remove(key) : Hold(key, holdIn);

        if (removed)
        {
            Changed?.Invoke();
        }

        return removed;
    }

    /// <summary>Forgets whatever is saved for this request.</summary>
    internal bool Forget(VoiceProfile profile, string text) =>
        CanKey(profile, text) && Forget(KeyFor(profile, text));

    /// <summary>
    /// Forgets every saved line. Returns how many there were. With
    /// <paramref name="holdIn"/> the lines are moved there rather than
    /// deleted - a rename on the same drive, however big the library - so
    /// <see cref="PutBackHeld"/> can undo it.
    /// </summary>
    internal int ForgetAll(string? holdIn = null)
    {
        var removed = 0;

        try
        {
            if (!Directory.Exists(_folder))
            {
                return 0;
            }

            foreach (var file in new DirectoryInfo(_folder).EnumerateFiles().ToList())
            {
                if (IsAudioFile(file.Name))
                {
                    if (holdIn is not null && UndoHoldingFolder.MoveIn(file.FullName, holdIn) is not null)
                    {
                        removed++;
                    }
                    else if (TryDelete(file.FullName))
                    {
                        removed++;
                    }
                }
                else if (IsInfoFile(file.Name))
                {
                    if (holdIn is null || UndoHoldingFolder.MoveIn(file.FullName, holdIn) is null)
                    {
                        TryDelete(file.FullName);
                    }
                }
                else if (file.Name.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(file.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[voice library] Couldn't clear every saved line ({ex.Message}).");
        }

        Changed?.Invoke();
        return removed;
    }

    /// <summary>How many lines are saved, and how much room they take. Reads the folder, so not for the UI thread.</summary>
    internal LibraryStats GetStats()
    {
        var lines = 0;
        long bytes = 0;

        try
        {
            if (!Directory.Exists(_folder))
            {
                return new LibraryStats(0, 0);
            }

            foreach (var file in new DirectoryInfo(_folder).EnumerateFiles())
            {
                if (IsAudioFile(file.Name))
                {
                    lines++;
                    bytes += file.Length;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What was counted before the folder stopped answering.
        }

        return new LibraryStats(lines, bytes);
    }

    /// <summary>
    /// The lines most recently played or saved, newest first, with their
    /// words. Reads the folder, so not for the UI thread.
    /// </summary>
    internal IReadOnlyList<SavedLine> Recent(int max)
    {
        var result = new List<SavedLine>();

        try
        {
            if (!Directory.Exists(_folder))
            {
                return result;
            }

            var newest = new DirectoryInfo(_folder)
                .EnumerateFiles()
                .Where(file => IsAudioFile(file.Name))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(0, max))
                .ToList();

            foreach (var file in newest)
            {
                var key = Path.GetFileNameWithoutExtension(file.Name);
                var info = ReadInfo(key);

                result.Add(new SavedLine(
                    key,
                    string.IsNullOrWhiteSpace(info?.Text) ? "(words not recorded)" : info!.Text,
                    info?.VoiceName ?? string.Empty,
                    file.LastWriteTimeUtc,
                    file.Length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Whatever was gathered before the folder stopped answering.
        }

        return result;
    }

    /// <summary>Runs one size check in the background, the first time the library is used in a session.</summary>
    internal void StartPruneOnce()
    {
        if (Interlocked.Exchange(ref _pruneStarted, 1) == 0)
        {
            _ = Task.Run(Prune);
        }
    }

    /// <summary>
    /// Keeps the library under <see cref="MaxBytes"/> by clearing the lines
    /// that have gone longest without being played, and sweeps up anything
    /// a crash left half-written.
    ///
    /// No age limit on its own: a club's summer break must not cost it every
    /// line it had. Only size decides.
    /// </summary>
    private void Prune()
    {
        lock (_pruneLock)
        {
            try
            {
                if (!Directory.Exists(_folder))
                {
                    return;
                }

                var now = DateTime.UtcNow;
                var audio = new List<FileInfo>();
                var infos = new List<FileInfo>();

                foreach (var file in new DirectoryInfo(_folder).EnumerateFiles())
                {
                    if (IsAudioFile(file.Name))
                    {
                        audio.Add(file);
                    }
                    else if (IsInfoFile(file.Name))
                    {
                        infos.Add(file);
                    }
                    else if (file.Name.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase)
                             && now - file.LastWriteTimeUtc > TimeSpan.FromHours(1))
                    {
                        // A save that never finished. An hour's grace, so a
                        // save in progress right now is not pulled from under
                        // itself.
                        TryDelete(file.FullName);
                    }
                }

                var total = audio.Sum(file => file.Length);
                var removed = 0;

                if (total > MaxBytes)
                {
                    foreach (var file in audio.OrderBy(f => f.LastWriteTimeUtc))
                    {
                        if (total <= PruneToBytes)
                        {
                            break;
                        }

                        if (Remove(Path.GetFileNameWithoutExtension(file.Name)))
                        {
                            total -= file.Length;
                            removed++;
                        }
                    }
                }

                // Words written for a line whose audio is gone - cleared
                // above, or deleted by hand.
                foreach (var info in infos)
                {
                    var key = Path.GetFileNameWithoutExtension(info.Name);

                    if (FindAudio(key) is null && now - info.LastWriteTimeUtc > TimeSpan.FromHours(1))
                    {
                        TryDelete(info.FullName);
                    }
                }

                if (removed > 0)
                {
                    Log?.Invoke($"[voice library] Over {MaxBytes / (1024 * 1024)} MB - cleared {Plural.Of(removed, "line")} that had gone longest without being played.");
                    Changed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[voice library] Tidy-up stopped early ({ex.Message}).");
            }
        }
    }

    /// <summary>
    /// Whether a line can be keyed at all: there has to be a voice to key
    /// it by. The HeatCaller voice has its own - the catalogue id the club
    /// picked - and no ElevenLabs voice id of its own, so without this a
    /// club on the HeatCaller voice would cache nothing and ask the relay
    /// for every line, every time.
    /// </summary>
    private static bool CanKey(VoiceProfile profile, string text) =>
        !string.IsNullOrWhiteSpace(text)
        && (profile.EffectiveEngine == VoiceEngine.HeatCaller
            ? !string.IsNullOrWhiteSpace(profile.HeatCallerVoice)
            : !string.IsNullOrWhiteSpace(profile.VoiceId));

    private string AudioPath(string key, string extension) => Path.Combine(_folder, key + extension);

    private string InfoPath(string key) => Path.Combine(_folder, key + InfoExtension);

    /// <summary>The saved file for a key, MP3 first - a line rendered on its own beats one cut from a batch.</summary>
    private string? FindAudio(string key)
    {
        try
        {
            foreach (var extension in new[] { Mp3Extension, WavExtension })
            {
                var path = AudioPath(key, extension);

                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Treated as not saved.
        }

        return null;
    }

    private SavedLineInfo? ReadInfo(string key)
    {
        try
        {
            var path = InfoPath(key);
            return File.Exists(path) ? JsonSerializer.Deserialize<SavedLineInfo>(File.ReadAllBytes(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves a key's audio and its words into a holding folder, for an undo.
    /// A file that can't be moved is deleted as Remove would. True when there
    /// was audio.
    /// </summary>
    private bool Hold(string key, string holdIn)
    {
        var removed = false;

        foreach (var path in new[] { AudioPath(key, Mp3Extension), AudioPath(key, WavExtension) })
        {
            if (UndoHoldingFolder.MoveIn(path, holdIn) is not null)
            {
                removed = true;
            }
            else
            {
                removed |= TryDelete(path);
            }
        }

        if (UndoHoldingFolder.MoveIn(InfoPath(key), holdIn) is null)
        {
            TryDelete(InfoPath(key));
        }

        return removed;
    }

    /// <summary>
    /// Puts lines forgotten into a holding folder back, for Undo. A line
    /// rendered again since keeps its newer take, and its words with it.
    /// Returns how many lines came back. Reads and moves files, so not for
    /// the UI thread with a big library - though a move on one drive is quick.
    /// </summary>
    internal int PutBackHeld(string holdIn)
    {
        var restored = 0;

        try
        {
            if (!Directory.Exists(holdIn))
            {
                return 0;
            }

            Directory.CreateDirectory(_folder);

            var files = new DirectoryInfo(holdIn).EnumerateFiles().ToList();

            // Audio first, so the words only follow a take that came back.
            foreach (var file in files.Where(f => IsAudioFile(f.Name)))
            {
                var key = Path.GetFileNameWithoutExtension(file.Name);
                if (FindAudio(key) is not null)
                {
                    continue;
                }

                if (UndoHoldingFolder.MoveBack(file.FullName, Path.Combine(_folder, file.Name)))
                {
                    restored++;
                }
            }

            foreach (var file in files.Where(f => IsInfoFile(f.Name)))
            {
                var key = Path.GetFileNameWithoutExtension(file.Name);
                if (FindAudio(key) is not null && !File.Exists(InfoPath(key)))
                {
                    UndoHoldingFolder.MoveBack(file.FullName, InfoPath(key));
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[voice library] Couldn't put the forgotten lines back ({ex.Message}).");
        }

        if (restored > 0)
        {
            Changed?.Invoke();
        }

        return restored;
    }

    /// <summary>Deletes a key's audio and its words. True when there was audio to delete.</summary>
    private bool Remove(string key)
    {
        var removed = TryDelete(AudioPath(key, Mp3Extension));
        removed |= TryDelete(AudioPath(key, WavExtension));
        TryDelete(InfoPath(key));
        return removed;
    }

    /// <summary>
    /// Written to a temporary file beside it, flushed to the disk, then
    /// moved into place - so a crash or a power cut mid-save leaves either
    /// the old file or the new one, never half of one that would then be
    /// played as noise. Also used by StingerLibrary for recorded soundscape
    /// lines (9 Nov 2026, Bug Hunt 15, B32).
    /// </summary>
    internal static void WriteAtomically(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + TempExtension;

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            // Only still there when the move did not happen.
            TryDelete(temp);
        }
    }

    /// <summary>True when the file existed and is now gone.</summary>
    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsAudioFile(string name) =>
        (name.EndsWith(Mp3Extension, StringComparison.OrdinalIgnoreCase)
         || name.EndsWith(WavExtension, StringComparison.OrdinalIgnoreCase))
        && IsKey(Path.GetFileNameWithoutExtension(name));

    private static bool IsInfoFile(string name) =>
        name.EndsWith(InfoExtension, StringComparison.OrdinalIgnoreCase)
        && IsKey(Path.GetFileNameWithoutExtension(name));

    /// <summary>A key is 64 lower-case hex characters - anything else in the folder is not ours and is left alone.</summary>
    private static bool IsKey(string? name)
    {
        if (name is null || name.Length != 64)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57] + "...";
}
