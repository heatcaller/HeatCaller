using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Recorded voice lines ("stingers") for the fixed race calls - staging,
/// countdown, the start, flags, last lap, the checkered flag - recorded once
/// per soundscape and then played from disk whenever that exact line is said.
///
/// WHY, when VoiceLibrary already keeps every ElevenLabs line after the
/// first time: VoiceLibrary is keyed by the voice settings too, so it only
/// helps while those stay the same, and it never helps a PC on the Windows
/// voice. A recorded line is keyed by its WORDS alone. Once Pit Lane's
/// "THERE'S THE BELL - LAST LAP!" is recorded, it plays in that recorded
/// voice on this PC whatever voice is chosen - including the free Windows
/// voice, and with no internet - so the big race moments always sound the
/// part. Lines with a driver's name or a time in them change every race and
/// are never recorded; they are spoken live as always.
///
/// MATCHING. By the line's words with audio tags ([excited]) and spacing
/// taken out and case ignored - the voice engine may or may not have
/// stripped the tags by the time the line reaches RenderLineAsync.
///
/// WHERE. %AppData%\HeatCaller\VoiceLines - beside the club's other
/// data rather than in %LocalAppData% with the VoiceLibrary cache, because
/// these were recorded on purpose and cost characters to make.
///
/// Never throws: a line that can't be read is simply spoken live.
/// </summary>
public sealed class StingerLibrary
{
    public sealed record Entry(string Key, string Text, string File, string VoiceName, string SoundscapeName, DateTime RecordedUtc);

    public static StingerLibrary Shared { get; } = new(Path.Combine(
        AppPaths.Root,
        "VoiceLines"));

    /// <summary>The race moments whose lines are recorded - the ones said the same way every race.</summary>
    public static readonly IReadOnlyList<AnnouncementEventType> FixedEvents = new[]
    {
        AnnouncementEventType.RaceStaging,
        AnnouncementEventType.CountdownStarted,
        AnnouncementEventType.RaceStarted,
        AnnouncementEventType.YellowFlag,
        AnnouncementEventType.RedFlag,
        AnnouncementEventType.GreenFlagResumed,
        AnnouncementEventType.RaceTimeOver,
        AnnouncementEventType.RaceCompleted,
    };

    private readonly string _folder;
    private readonly object _gate = new();
    private Dictionary<string, Entry>? _index;

    // Set while index.json exists but could not be read (held open by a sync
    // or backup tool, usually). The index is then NOT cached: an empty one
    // cached for the session spoke every recorded line live all night, and
    // the next recording saved an index of just this session's lines over
    // every other soundscape's - recordings the club paid for, orphaned for
    // good. Lines recorded meanwhile wait in _unreadIndex and are merged in
    // once the file reads; nothing is written over it until then.
    private Dictionary<string, Entry>? _unreadIndex;
    private bool _holdWrites;
    private bool _reportedUnreadable;
    private DateTime _nextReadAttemptUtc;

    private StingerLibrary(string folder)
    {
        _folder = folder;
    }

    private string IndexPath => Path.Combine(_folder, "index.json");

    /// <summary>The matching key for a line - see the class summary.</summary>
    public static string KeyFor(string text)
    {
        var plain = VoiceProfile.StripAudioTags(text ?? string.Empty);
        var collapsed = string.Join(' ', plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return collapsed;
    }

    /// <summary>
    /// The lines of a soundscape that can be recorded: every line of the
    /// fixed moments that are switched on, with nothing filled in at race time.
    /// </summary>
    public static IReadOnlyList<string> RecordableLines(VoiceProfile voice)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>();

        foreach (var type in FixedEvents)
        {
            // Only the moments that are switched on - a line that is never
            // said is not worth paying for. Switch one on later and its lines
            // show up as missing, ready to record.
            if (!voice.Events.TryGetValue(type.ToString(), out var config) || config?.Templates is null || !config.Enabled)
            {
                continue;
            }

            foreach (var line in config.Templates)
            {
                if (string.IsNullOrWhiteSpace(line) || line.Contains('{'))
                {
                    continue;
                }

                var key = KeyFor(line);
                if (key.Length > 0 && seen.Add(key))
                {
                    lines.Add(line.Trim());
                }
            }
        }

        return lines;
    }

    private Dictionary<string, Entry> Index()
    {
        if (_index is not null)
        {
            return _index;
        }

        // Still unreadable a moment ago: don't make every line wait out the
        // read retries again - try again every little while instead.
        if (_unreadIndex is not null && DateTime.UtcNow < _nextReadAttemptUtc)
        {
            return _unreadIndex;
        }

        var index = new Dictionary<string, Entry>();
        try
        {
            if (File.Exists(IndexPath))
            {
                var entries = JsonSerializer.Deserialize<List<Entry>>(SafeFile.ReadAllTextWithRetry(IndexPath));
                foreach (var entry in entries ?? new List<Entry>())
                {
                    // Only a file name Save could have written. The index is
                    // just a text file in the club's folder: an entry naming
                    // "..\..\x", "C:\x" or "\\server\share\x" would
                    // otherwise have "Delete recorded lines" delete that file,
                    // or have playing a line log on to someone else's server.
                    if (entry is not null && !string.IsNullOrEmpty(entry.Key) && IsSavedFileName(entry.File))
                    {
                        index[entry.Key] = entry;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Reported (and a copy kept) once, not on every retry.
            if (!_reportedUnreadable)
            {
                _reportedUnreadable = true;
                _holdWrites = SafeFile.HoldWritesAfterUnreadable(IndexPath, ex);
            }

            if (_holdWrites)
            {
                // Could not be read - see _unreadIndex. Not cached, so the
                // next look after a short wait tries the file again.
                _unreadIndex ??= index;
                _nextReadAttemptUtc = DateTime.UtcNow.AddSeconds(30);
                return _unreadIndex;
            }

            // Damaged content, and a copy of it was kept aside: start empty
            // as before. The audio files are still on disk, and recording
            // again rewrites the index.
        }

        _holdWrites = false;
        _index = index;

        // Lines recorded while the file couldn't be read: merge them in
        // (they are the newer takes) and write the whole index now.
        if (_unreadIndex is { Count: > 0 } pending)
        {
            foreach (var entry in pending.Values)
            {
                index[entry.Key] = entry;
            }

            SafeFile.WriteAllText(IndexPath, JsonSerializer.Serialize(index.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }

        _unreadIndex = null;
        return index;
    }

    /// <summary>
    /// Whether a name is exactly what <see cref="Save"/> writes: 40 lowercase
    /// hex characters (a SHA-1) and ".audio".
    /// </summary>
    private static bool IsSavedFileName(string? file)
    {
        const string extension = ".audio";
        if (file is null || file.Length != 40 + extension.Length || !file.EndsWith(extension, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 0; i < 40; i++)
        {
            if (!(char.IsAsciiDigit(file[i]) || file[i] is >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The path of a recorded line's file, or null if it wouldn't be inside the folder.</summary>
    private string? PathFor(Entry entry)
    {
        if (!IsSavedFileName(entry.File))
        {
            return null;
        }

        var path = Path.Combine(_folder, entry.File);
        return PlainFileName.IsInside(_folder, path) ? path : null;
    }

    private void SaveIndex()
    {
        var index = Index();

        // index.json is there but couldn't be read: saving now would write
        // just this session's lines over everyone else's. They are kept in
        // memory and written once the file can be read - see Index().
        if (_index is null && _holdWrites)
        {
            return;
        }

        SafeFile.WriteAllText(IndexPath, JsonSerializer.Serialize(index.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
    }

    public bool Has(string text)
    {
        lock (_gate)
        {
            return Index().ContainsKey(KeyFor(text));
        }
    }

    /// <summary>How many of these lines are recorded.</summary>
    public int CountRecorded(IEnumerable<string> lines)
    {
        lock (_gate)
        {
            var index = Index();
            return lines.Count(l => index.ContainsKey(KeyFor(l)));
        }
    }

    /// <summary>The recorded audio for a line, or null to speak it live.</summary>
    public byte[]? TryLoad(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Entry? entry;
        lock (_gate)
        {
            if (!Index().TryGetValue(KeyFor(text), out entry))
            {
                return null;
            }
        }

        try
        {
            var path = PathFor(entry);
            return path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string text, byte[] audio, string voiceName, string soundscapeName)
    {
        if (string.IsNullOrWhiteSpace(text) || audio.Length == 0)
        {
            return;
        }

        var key = KeyFor(text);
        var file = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".audio";

        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllBytes(Path.Combine(_folder, file), audio);
        }
        catch
        {
            return;
        }

        lock (_gate)
        {
            Index()[key] = new Entry(key, text.Trim(), file, voiceName, soundscapeName, DateTime.UtcNow);
            SaveIndex();
        }
    }

    /// <summary>A recording set aside for Undo: its index entry, and where its audio was moved to (null if it had none on disk).</summary>
    public sealed record Held(Entry Entry, string? HeldFile);

    /// <summary>
    /// Forgets these lines' recordings as Delete does, but MOVES the audio
    /// into the undo holding folder instead of deleting it, and hands back
    /// what was set aside so <see cref="PutBack"/> can restore it. Recordings
    /// cost characters to make, so throwing them away on a mis-click - or
    /// before a re-record that comes out worse - is worth being able to undo.
    /// </summary>
    public IReadOnlyList<Held> DeleteForUndo(IEnumerable<string> lines)
    {
        var held = new List<Held>();
        string? holdIn = null;

        lock (_gate)
        {
            var index = Index();
            foreach (var line in lines)
            {
                if (!index.Remove(KeyFor(line), out var entry))
                {
                    continue;
                }

                string? heldFile = null;

                // Never a file outside the folder - see Index().
                if (PathFor(entry) is { } path && File.Exists(path))
                {
                    holdIn ??= UndoHoldingFolder.NewFolder();
                    heldFile = UndoHoldingFolder.MoveIn(path, holdIn);

                    if (heldFile is null)
                    {
                        // Couldn't be held - forgotten as before, and this one
                        // line simply won't come back with an undo.
                        try
                        {
                            File.Delete(path);
                        }
                        catch
                        {
                            // An orphaned file costs disk space, nothing else.
                        }
                    }
                }

                held.Add(new Held(entry, heldFile));
            }

            if (held.Count > 0)
            {
                SaveIndex();
            }
        }

        return held;
    }

    /// <summary>
    /// Puts recordings set aside by <see cref="DeleteForUndo"/> back.
    ///
    /// <paramref name="replaceNewer"/> decides what happens to a line recorded
    /// again since: false leaves the newer take (undoing a plain delete must
    /// not throw away a recording made afterwards); true puts the old take
    /// back over it (undoing a re-record, where replacing it is the point).
    /// Returns how many came back.
    /// </summary>
    public int PutBack(IEnumerable<Held> held, bool replaceNewer)
    {
        var restored = 0;

        lock (_gate)
        {
            var index = Index();
            foreach (var item in held)
            {
                if (index.ContainsKey(item.Entry.Key) && !replaceNewer)
                {
                    continue;
                }

                var path = PathFor(item.Entry);
                if (path is null || !UndoHoldingFolder.MoveBack(item.HeldFile, path, overwrite: true))
                {
                    continue;
                }

                index[item.Entry.Key] = item.Entry;
                restored++;
            }

            if (restored > 0)
            {
                SaveIndex();
            }
        }

        return restored;
    }

    /// <summary>Forgets these lines' recordings, so they are spoken live again.</summary>
    public int Delete(IEnumerable<string> lines)
    {
        var removed = 0;
        lock (_gate)
        {
            var index = Index();
            foreach (var line in lines)
            {
                if (index.Remove(KeyFor(line), out var entry))
                {
                    removed++;
                    try
                    {
                        // Never a file outside the folder - see Index().
                        var path = PathFor(entry);
                        if (path is not null)
                        {
                            File.Delete(path);
                        }
                    }
                    catch
                    {
                        // An orphaned file costs disk space, nothing else.
                    }
                }
            }

            if (removed > 0)
            {
                SaveIndex();
            }
        }

        return removed;
    }
}
