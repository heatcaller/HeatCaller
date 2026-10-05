using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Sponsor/promo slides shown in rotation on the Big Screen between
/// leaderboard views - useful at a commercial venue, easily left empty and
/// ignored at home. Image files are copied into a dedicated folder (like
/// track attachments) rather than referenced in place, so moving or
/// deleting the original doesn't silently break the display mid-event.
/// </summary>
public sealed class SponsorStore
{
    private SponsorSettings _settings;

    private static string RootFolder => AppPaths.Root;

    private static string FilePath => Path.Combine(RootFolder, "sponsors.json");
    private static string ImageFolder => Path.Combine(RootFolder, "Sponsors");

    private SponsorStore(SponsorSettings settings)
    {
        _settings = settings;
    }

    public static SponsorStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<SponsorSettings>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (loaded is not null)
                {
                    // Slide names are joined onto the Sponsors folder and
                    // served to anyone on the club network. A name that is
                    // anything but a plain file name - from a hand-edited
                    // file or a restored backup - is dropped here, once, so
                    // nothing further along ever sees it.
                    loaded.SlideFileNames = (loaded.SlideFileNames ?? new List<string>())
                        .Where(PlainFileName.IsSafeLeaf)
                        .ToList();
                    return new SponsorStore(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            // But, when it could not be read at all or no copy of it could be
            // kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new SponsorStore(new SponsorSettings()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. A briefly locked file used to load empty and the
    /// next save replaced the club's sponsor slide list with nothing.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // these defaults would replace it. The console has already said why.
        if (LoadedUnreadable)
        {
            return;
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public SponsorSettings Current => _settings;

    public IReadOnlyList<string> SlideFileNames => _settings.SlideFileNames;

    /// <summary>Full path to a stored slide, or null if the file has gone missing.</summary>
    public string? ResolvePath(string fileName)
    {
        var path = SlidePath(ImageFolder, fileName);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// A slide's path inside <paramref name="folder"/>, or null if the name
    /// could point anywhere else. Asked before every read, move and delete -
    /// see PlainFileName.
    /// </summary>
    private static string? SlidePath(string folder, string fileName)
    {
        if (!PlainFileName.IsSafeLeaf(fileName))
        {
            return null;
        }

        var path = Path.Combine(folder, fileName);
        return PlainFileName.IsInside(folder, path) ? path : null;
    }

    /// <summary>
    /// The stored name for a newly added slide: a short unique prefix, so
    /// two files with the same name from different folders don't collide,
    /// then as much of the original name as passes IsSafeLeaf - kept
    /// because it's what the user recognises in the list.
    /// </summary>
    private static string NewSlideFileName(string sourceFilePath)
    {
        var prefix = Guid.NewGuid().ToString("N")[..8];
        var original = Path.GetFileName(sourceFilePath);
        var extension = Path.GetExtension(original);
        var stem = Path.GetFileNameWithoutExtension(original).Trim(' ', '.');

        if (extension.Length > 16 || !PlainFileName.IsSafeLeaf(prefix + extension))
        {
            extension = string.Empty;
        }

        var room = 200 - prefix.Length - 1 - extension.Length;
        if (stem.Length > room)
        {
            stem = stem[..room].TrimEnd(' ', '.');
        }

        var name = $"{prefix}_{stem}{extension}";
        return PlainFileName.IsSafeLeaf(name) ? name : prefix + extension;
    }

    /// <summary>
    /// Copies an image in from wherever the user picked it. Returns the
    /// stored filename, or null with the reason in <paramref name="error"/>.
    /// </summary>
    public string? AddSlide(string sourceFilePath, out string? error)
    {
        try
        {
            // Sized up from its length and its header first (see
            // ImageProbe). Any file used to be copied in unlooked-at, and a
            // slide is decoded whole every time the Sponsors page shows it
            // and every time it comes round on the Big Screen - so a huge
            // photo, or a small file whose header claims tens of thousands
            // of pixels each way, froze the app there, rotation after
            // rotation, or crashed it out of memory.
            if (ImageProbe.PickedProblem(sourceFilePath, "show as a sponsor slide", "Choose a smaller copy.", out var bytes) is { } problem)
            {
                error = problem;
                return null;
            }

            Directory.CreateDirectory(ImageFolder);

            // Prefix with a short unique id so two files with the same name
            // from different folders don't collide and overwrite each other.
            var fileName = NewSlideFileName(sourceFilePath);

            // The bytes that were checked rather than a second copy of the
            // file, so what is stored is exactly what passed.
            File.WriteAllBytes(Path.Combine(ImageFolder, fileName), bytes);

            _settings.SlideFileNames.Add(fileName);
            Save();
            error = null;
            return fileName;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// A stored slide's bytes, ready to decode - or null when the file is
    /// more than AddSlide takes in: too long, not a PNG, JPEG, GIF or BMP,
    /// or by its header too many pixels. A slide is kept exactly as it was
    /// picked, so this lets through everything AddSlide does and nothing
    /// bigger; what it stops is one planted in a restored backup, or one
    /// added before AddSlide looked. For the Big Screen and the Sponsors
    /// page, which both used to decode whatever the file held. Throws when
    /// the file can't be read at all; both callers catch that.
    /// </summary>
    public static byte[]? ReadSlide(string path) => ImageProbe.ReadStored(path);

    private static string StashFolder => Path.Combine(ImageFolder, "_removed");

    /// <summary>
    /// Removes a slide from the rotation, moving its image aside rather
    /// than deleting it so the removal can be undone. Deleting outright
    /// would restore the rotation entry on undo but leave it pointing at
    /// a file that no longer exists.
    ///
    /// Aside means the app's undo holding folder (see UndoHoldingFolder),
    /// one folder per removal, so several removals can each be undone and
    /// old ones are cleared after a week. Returns where the image went, for
    /// RestoreSlide - null when it had none or it couldn't be moved (a
    /// locked file just lingers where it was).
    /// </summary>
    public string? RemoveSlide(string fileName)
    {
        var index = _settings.SlideFileNames.IndexOf(fileName);
        _settings.SlideFileNames.Remove(fileName);
        Save();

        string? held = null;
        var path = SlidePath(ImageFolder, fileName);
        if (path is not null && File.Exists(path))
        {
            held = UndoHoldingFolder.MoveIn(path, UndoHoldingFolder.NewFolder());
        }

        _lastRemovedIndex = index;
        return held;
    }

    /// <summary>Where the last removed slide sat in the rotation, so undo can put it back in the same place.</summary>
    private int _lastRemovedIndex = -1;

    /// <summary>
    /// Restores a removed slide to the rotation, at the place it held. Pairs
    /// with RemoveSlide to undo a removal: <paramref name="held"/> is what
    /// RemoveSlide returned. Without it, the old per-name stash is looked in
    /// (removals made by an older version). A slide already back in the
    /// rotation is not listed twice.
    /// </summary>
    public void RestoreSlide(string fileName, int index, string? held = null)
    {
        // Only a name that could have been removed can come back.
        if (!PlainFileName.IsSafeLeaf(fileName))
        {
            return;
        }

        var restored = SlidePath(ImageFolder, fileName);
        if (held is not null && restored is not null)
        {
            UndoHoldingFolder.MoveBack(held, restored);
        }
        else
        {
            try
            {
                var stashed = SlidePath(StashFolder, fileName);
                if (stashed is not null && restored is not null && File.Exists(stashed))
                {
                    Directory.CreateDirectory(ImageFolder);
                    File.Move(stashed, restored, overwrite: true);
                }
            }
            catch
            {
                // The rotation entry still comes back either way.
            }
        }

        if (!_settings.SlideFileNames.Contains(fileName))
        {
            var at = index >= 0 && index <= _settings.SlideFileNames.Count ? index : _settings.SlideFileNames.Count;
            _settings.SlideFileNames.Insert(at, fileName);
        }

        Save();
    }

    /// <summary>Index the most recent RemoveSlide took the file from.</summary>
    public int LastRemovedIndex => _lastRemovedIndex;

    /// <summary>
    /// Raised after an undo has changed the slides or the timing, so the
    /// Sponsors page re-reads the store. Not raised by ordinary saves.
    /// </summary>
    public event Action? Restored;

    /// <summary>Tells whatever shows this store that an undo just changed it. UI thread.</summary>
    public void NotifyRestored() => Restored?.Invoke();

    public void Move(string fileName, int direction)
    {
        var index = _settings.SlideFileNames.IndexOf(fileName);
        var newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= _settings.SlideFileNames.Count)
        {
            return;
        }

        (_settings.SlideFileNames[index], _settings.SlideFileNames[newIndex]) =
            (_settings.SlideFileNames[newIndex], _settings.SlideFileNames[index]);
        Save();
    }
}

public sealed class SponsorSettings
{
    /// <summary>Whether to show sponsor slides at all. Off by default - a home setup has no use for this.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long the leaderboard shows before cutting to a sponsor slide.</summary>
    public int LeaderboardSeconds { get; set; } = 60;

    /// <summary>How long each sponsor slide stays up before returning to the leaderboard.</summary>
    public int SlideSeconds { get; set; } = 8;

    /// <summary>Never interrupt the leaderboard to show a slide while a race is actually running - on by default, since mid-race is exactly when people need the live standings.</summary>
    public bool PauseDuringRace { get; set; } = true;

    public List<string> SlideFileNames { get; set; } = new();
}
