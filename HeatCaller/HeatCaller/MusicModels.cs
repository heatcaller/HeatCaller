using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller;

/// <summary>What the music does when a session ends.</summary>
public enum MusicEndBehaviour
{
    /// <summary>Carry on - the gap between heats is when a soundtrack is most wanted.</summary>
    KeepPlaying,

    /// <summary>Fade down gently once the recap has finished.</summary>
    FadeOut,

    /// <summary>Stop immediately once the recap has finished.</summary>
    StopImmediately,
}

/// <summary>One track in the music library.</summary>
public sealed class MusicTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// True for the tracks that ship with the app, false for anything the
    /// user added. Only used to label them in the list and to stop a
    /// built-in being deleted when the file isn't the user's to remove.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(FilePath) : Title;
}

/// <summary>
/// Music settings - the club's between-race soundtrack.
///
/// Deliberately separate from Ambience: ambience is a reactive bed that
/// follows the race, whereas music is a playlist someone puts on. Folding
/// them together would mean a track swelling at a fast lap, which is not
/// what music is for.
/// </summary>
public sealed class MusicSettings
{
    /// <summary>Off by default - it should be a deliberate choice to start playing music at an event.</summary>
    public bool Enabled { get; set; }

    public float Volume { get; set; } = 0.4f;

    /// <summary>Play tracks in random order rather than list order.</summary>
    public bool Shuffle { get; set; } = true;

    /// <summary>
    /// Stop music while a race is running, resuming between races.
    ///
    /// Off by default now: with ducking handling the announcer, music
    /// through a heat is a deliberate choice rather than a problem, and
    /// cutting it at the green flag surprised people who wanted a
    /// soundtrack for the whole session.
    /// </summary>
    public bool PauseDuringRaces { get; set; }

    /// <summary>
    /// Keep playing after the checkered flag until someone presses stop.
    ///
    /// A race ending is not a reason for the music to stop - the gap
    /// between heats is exactly when a soundtrack earns its keep. Only an
    /// explicit press of Stop ends it.
    /// </summary>
    public bool ContinueAfterRace { get; set; } = true;

    /// <summary>Duck under announcements, the same way ambience does.</summary>
    public bool DuckUnderAnnouncements { get; set; } = true;

    public float DuckToFraction { get; set; } = 0.25f;

    /// <summary>
    /// What the player plays: true for the applied soundscape's own tracks
    /// ("Theme music"), false for the enabled tracks in the library ("My
    /// music").
    ///
    /// Null only in a file written before the choice existed, and settled on
    /// the first load after (see MusicStore.SettleMusicSource): theme music
    /// when a soundscape with music had been applied - that is what those
    /// clubs were hearing - and their own music otherwise.
    /// </summary>
    public bool? PlayThemeMusic { get; set; }

    public List<MusicTrack> Tracks { get; set; } = new();
}

/// <summary>
/// The applied soundscape's music, for the Music page's "Theme music": its
/// name, to say where the music comes from, and its tracks by title. Titles
/// rather than tracks because that is how a soundscape names them - they are
/// matched against the library when the queue is built.
/// </summary>
public sealed record ThemeMusicSource(string SoundscapeName, IReadOnlyList<string> Titles)
{
    /// <summary>The library's tracks this soundscape plays, in library order, that are on disk.</summary>
    public List<MusicTrack> TracksIn(IEnumerable<MusicTrack> library) =>
        library
            .Where(t => Titles.Contains(t.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase) && File.Exists(t.FilePath))
            .ToList();
}

/// <summary>Loads and saves the music library.</summary>
public sealed class MusicStore
{
    private static string SettingsDirectory => AppPaths.Root;

    private static string FilePath => Path.Combine(SettingsDirectory, "music.json");

    /// <summary>
    /// Where the bundled tracks live, and where uploads are copied to.
    ///
    /// The shipped soundtrack is WAV - uncompressed, so it's exactly what
    /// was authored with no encoder in between. Uploads may be WAV or MP3;
    /// the player treats them the same.
    ///
    /// Copying rather than referencing the original path means a track
    /// still plays after someone clears their Downloads folder - a real
    /// risk when the obvious place to grab a file from is wherever it was
    /// just downloaded.
    /// </summary>
    public static string MusicDirectory => Path.Combine(SettingsDirectory, "Music");

    public MusicSettings Current { get; private set; } = new();

    /// <summary>
    /// Set when music.json was there but couldn't be read - see
    /// SafeFile.HoldWritesAfterUnreadable. Nothing is saved over it this
    /// session: it holds the club's own tracks, which the sync never lists
    /// again by itself, and a file locked for a moment at launch used to
    /// load as the bundled tracks alone and be replaced by the first change.
    /// </summary>
    public bool LoadedUnreadable { get; private set; }

    public static MusicStore Load()
    {
        var store = new MusicStore();
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var text = SafeFile.ReadAllTextWithRetry(FilePath);
                var loaded = JsonSerializer.Deserialize<MusicSettings>(text);
                if (loaded is not null)
                {
                    // Read before the nulls are filtered out, so position i
                    // here is still position i in the file.
                    var rawTracks = loaded.Tracks ?? new List<MusicTrack>();
                    var musicFolderPaths = ReadMusicFolderPaths(text);

                    // Turned back into full paths before anything looks at
                    // them - see FromStoredPaths. Everything else in the app
                    // only ever sees full paths.
                    for (var i = 0; i < rawTracks.Count; i++)
                    {
                        var track = rawTracks[i];
                        if (track is null)
                        {
                            continue;
                        }

                        var inMusicFolder = musicFolderPaths is not null && i < musicFolderPaths.Count
                            ? musicFolderPaths[i]
                            : null;

                        track.FilePath = FromStoredPaths(track.FilePath, inMusicFolder);
                    }

                    loaded.Tracks = rawTracks
                        .Where(t => t is not null)
                        .Where(t => !IsForeignNetworkPath(t.FilePath))
                        .ToList();
                    store.Current = loaded;
                }
                else
                {
                    // Present, parsed, and not the shape this store writes.
                    // Not a new install either, so the same rule applies. A
                    // file holding the literal "null" parses to nothing
                    // without throwing: it used to leave the flag false, so
                    // the start-up sync below wrote the bundled tracks alone
                    // over the club's own library before anyone had seen a
                    // word about it.
                    unreadable = true;
                }
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start with defaults rather than refusing to open.
            // But, when it could not be read at all or no copy of it could be
            // kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        store.LoadedUnreadable = unreadable;
        store.InstallBundledTracks();

        // The sync saves - normally just to record what it found. After a
        // failed load it would be saving an empty library over the user's
        // real one before anyone had seen the message. (Save itself refuses
        // as well - see LoadedUnreadable.)
        store.SyncBuiltInTracks(save: !unreadable);
        return store;
    }

    /// <summary>
    /// A track path on another machine, other than the music folder itself.
    ///
    /// Every track is copied into the music folder when added, so a path
    /// on a server can only have come from a hand-edited or restored file -
    /// and even checking it exists makes Windows log on to that server (see
    /// PlainFileName.IsNetworkPath). The music folder is exempt because
    /// %AppData% itself is on a server wherever a network redirects it.
    /// </summary>
    private static bool IsForeignNetworkPath(string? path) =>
        PlainFileName.IsNetworkPath(path) && !PlainFileName.IsInside(MusicDirectory, path!);

    /// <summary>
    /// The extra field in each track's entry in music.json holding its path
    /// inside the music folder (see ToStoredPath). Absent for a track kept
    /// anywhere else, and in files written before 0.46.6.
    ///
    /// A field of its own rather than FilePath itself: 0.46.0-0.46.5 wrote
    /// the bare name into FilePath, and an older copy of the app reading
    /// that file - on a second laptop, say - saw every track as a name with
    /// no folder and could play none of them. FilePath goes back to holding
    /// the full path, as every version expects; older versions ignore a
    /// field they don't know.
    /// </summary>
    private const string MusicFolderPathField = "MusicFolderPath";

    /// <summary>
    /// How a track's path inside the music folder is written to music.json
    /// (in MusicFolderPathField): relative to the music folder when the file
    /// is in it (every track added through the app is), or null otherwise.
    ///
    /// Only the full path used to be stored, and it names this Windows
    /// account ("C:\Users\Sam\AppData\..."). A backup restored on
    /// another PC or account then pointed every track at a folder that isn't
    /// there: the tracks showed as missing, the sync added each file again
    /// as a new track, and every enabled/disabled choice and title edit was
    /// lost with the old entries.
    /// </summary>
    private static string? ToStoredPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !PlainFileName.IsInside(MusicDirectory, path))
        {
            return null;
        }

        return Path.GetRelativePath(MusicDirectory, path);
    }

    /// <summary>
    /// Each track's MusicFolderPathField, by its position in the file's
    /// Tracks list - null where a track has none. Null altogether when the
    /// file can't be read that way, in which case every track falls back to
    /// its FilePath exactly as before this field existed.
    /// </summary>
    private static List<string?>? ReadMusicFolderPaths(string json)
    {
        try
        {
            if (JsonNode.Parse(json)?[nameof(MusicSettings.Tracks)] is not JsonArray tracks)
            {
                return null;
            }

            var paths = new List<string?>(tracks.Count);

            foreach (var track in tracks)
            {
                paths.Add(track is JsonObject entry
                          && entry[MusicFolderPathField] is JsonValue value
                          && value.TryGetValue<string>(out var path)
                    ? path
                    : null);
            }

            return paths;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// A track's full path from both fields in music.json.
    ///
    /// The music-folder path wins when the file is there - that is the one
    /// that survives a move to another PC or account. Otherwise the stored
    /// FilePath is used as before (see FromStoredPath), unless that only
    /// leads somewhere this PC doesn't have, in which case the track is
    /// shown as missing from this music folder: where it belongs, and where
    /// putting the file back makes it play. A music-folder path that tries
    /// to climb out of the music folder is ignored.
    /// </summary>
    private static string FromStoredPaths(string? stored, string? inMusicFolder)
    {
        string? fromMusicFolder = null;

        try
        {
            if (!string.IsNullOrEmpty(inMusicFolder) && !Path.IsPathRooted(inMusicFolder))
            {
                var candidate = FromStoredPath(inMusicFolder);
                if (PlainFileName.IsInside(MusicDirectory, candidate))
                {
                    fromMusicFolder = candidate;
                }
            }

            if (fromMusicFolder is not null && File.Exists(fromMusicFolder))
            {
                return fromMusicFolder;
            }
        }
        catch
        {
            // A name Windows can't make sense of: fall back to FilePath.
            fromMusicFolder = null;
        }

        var fromFullPath = FromStoredPath(stored);

        if (fromMusicFolder is null)
        {
            return fromFullPath;
        }

        try
        {
            // Never File.Exists on a network path - that alone makes Windows
            // log on to the server (see IsForeignNetworkPath).
            if (PlainFileName.IsInside(MusicDirectory, fromFullPath)
                || (!PlainFileName.IsNetworkPath(fromFullPath) && File.Exists(fromFullPath)))
            {
                return fromFullPath;
            }
        }
        catch
        {
            // Unreadable path: the music folder's is the better guess.
        }

        return fromMusicFolder;
    }

    /// <summary>
    /// A track path as read from music.json, made into the full path the
    /// rest of the app uses.
    ///
    /// A bare name (what ToStoredPath writes, and what 0.46.0-0.46.5 wrote
    /// into FilePath itself) is in this account's music folder. A full path written by an older version, or on another PC,
    /// that points into SOME account's "HeatCaller\Music" folder is
    /// moved to this one's - when the file is actually here - so a restored
    /// library keeps its tracks and their settings. Anything else is left
    /// exactly as it was.
    /// </summary>
    private static string FromStoredPath(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }

        try
        {
            if (!Path.IsPathRooted(stored))
            {
                // A plain name is combined as it stands, so it comes out
                // exactly as Directory.GetFiles spells the same file - the
                // SyncBuiltInTracks matches tracks to files by comparing the two.
                var inMusicFolder = PlainFileName.IsSafeLeaf(stored)
                    ? Path.Combine(MusicDirectory, stored)
                    : Path.GetFullPath(Path.Combine(MusicDirectory, stored));

                return inMusicFolder;
            }

            // Never on a network path: even asking whether the file there
            // exists would make Windows log on to that server. The check
            // below only ever touches this account's own music folder.
            var separator = Path.DirectorySeparatorChar;
            var normalised = stored.Replace(Path.AltDirectorySeparatorChar, separator);
            var marker = $"{separator}HeatCaller{separator}Music{separator}";
            var at = normalised.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);

            if (at < 0 || PlainFileName.IsInside(MusicDirectory, stored))
            {
                return stored;
            }

            var name = normalised[(at + marker.Length)..];
            if (!PlainFileName.IsSafeLeaf(name))
            {
                return stored;
            }

            var here = Path.Combine(MusicDirectory, name);
            return File.Exists(here) ? here : stored;
        }
        catch
        {
            // A path Windows can't make sense of is left as it was, and
            // shows as missing in the Music tab like any other.
            return stored;
        }
    }

    /// <summary>
    /// Copies any track shipped with the app into the music folder, once.
    ///
    /// Installed rather than played from the install directory so the built
    /// -in tracks and the user's own uploads sit together - one folder to
    /// look in, and nothing breaks if the app is reinstalled elsewhere.
    /// Existing files are never overwritten, so a user who has renamed or
    /// replaced one keeps their version.
    /// </summary>
    /// <summary>
    /// Re-runs the bundled-track install and library sync, for the Music
    /// tab's "Restore Included Tracks" button. Public so a failed or
    /// skipped first-run install has a visible recovery path rather than
    /// leaving someone with an empty library and no explanation.
    /// </summary>
    public void ReinstallBundledTracks()
    {
        InstallBundledTracks();
        SyncBuiltInTracks(save: true);
    }

    /// <summary>
    /// Describes where the app looked for bundled tracks and what it found,
    /// so an empty library can be diagnosed instead of guessed at. The
    /// difference between "the build didn't include them" and "they're
    /// there but weren't picked up" is invisible otherwise.
    /// </summary>
    public string DescribeBundledSearch()
    {
        var lines = new List<string>();

        foreach (var dir in BundledCandidates())
        {
            if (!Directory.Exists(dir))
            {
                lines.Add($"NOT FOUND   {dir}");
                continue;
            }

            var audio = Directory.GetFiles(dir).Where(IsSupportedAudio).ToList();
            lines.Add(audio.Count > 0
                ? $"{Plural.Of(audio.Count, "track")}  {dir}"
                : $"no audio    {dir}");
        }

        lines.Add(string.Empty);
        lines.Add($"Music folder: {MusicDirectory}");
        lines.Add(Directory.Exists(MusicDirectory)
            ? $"  contains {Plural.Of(Directory.GetFiles(MusicDirectory).Count(IsSupportedAudio), "audio file")}"
            : "  does not exist yet");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The places a bundled track might be, in priority order.</summary>
    private static IEnumerable<string> BundledCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "Music");

        // MSBuild flattens CopyToOutputDirectory items unless the item
        // carries a Link - an older build will have put them straight next
        // to the executable.
        yield return AppContext.BaseDirectory;

        // Running from source: `dotnet run --project X` leaves the working
        // directory at the SOLUTION folder, so the assets sit one level
        // deeper under the project folder. Both are checked because which
        // applies depends on where the command was run from.
        var cwd = Directory.GetCurrentDirectory();
        yield return Path.Combine(cwd, "Assets", "Music");
        yield return Path.Combine(cwd, "HeatCaller", "Assets", "Music");

        // Walking up from the build output covers bin/Debug/net10.0-windows
        // back to the project folder, where a run-from-source build has
        // them even when the copy-to-output step didn't fire.
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Music"));
    }

    private void InstallBundledTracks()
    {
        try
        {
            // Several candidates rather than one fixed path. Exactly which
            // of these the tracks land in depends on how the project was
            // built and published, and a single guess that happens to be
            // wrong installs nothing with no visible reason why.
            var bundled = BundledCandidates().FirstOrDefault(dir =>
                Directory.Exists(dir) && Directory.GetFiles(dir).Any(IsSupportedAudio));

            if (bundled is null)
            {
                return;
            }

            Directory.CreateDirectory(MusicDirectory);

            foreach (var source in Directory.GetFiles(bundled).Where(IsSupportedAudio))
            {
                var target = Path.Combine(MusicDirectory, Path.GetFileName(source));
                if (!File.Exists(target))
                {
                    File.Copy(source, target);
                }
            }
        }
        catch
        {
            // Best-effort - a failed install must not stop the app opening.
        }
    }

    /// <summary>
    /// Adds any bundled track present on disk that isn't already listed,
    /// and drops built-in entries whose file has gone.
    ///
    /// Runs on every load so the library reflects what's actually there -
    /// a track added in a later version appears without the user doing
    /// anything, and one that's been deleted stops showing as playable.
    /// </summary>
    private void SyncBuiltInTracks(bool save)
    {
        try
        {
            if (!Directory.Exists(MusicDirectory))
            {
                return;
            }

            var onDisk = Directory.GetFiles(MusicDirectory)
                .Where(f => IsSupportedAudio(f))
                .ToList();

            foreach (var file in onDisk)
            {
                var known = Current.Tracks.Any(t =>
                    string.Equals(t.FilePath, file, StringComparison.OrdinalIgnoreCase));

                // Only the shipped tracks, as the summary says. Every audio
                // file in the folder used to be listed, so a club's own track
                // taken out with Remove (or Clear Library, keeping the files)
                // came back at the next launch, switched on and labelled
                // "Included". Their own files are listed by Add Track, which
                // also takes one already in the music folder.
                if (!known && IsBundledTitle(TitleForFile(file)))
                {
                    Current.Tracks.Add(new MusicTrack
                    {
                        Title = TitleForFile(file),
                        FilePath = file,
                        IsBuiltIn = true,
                    });
                }
            }

            // Re-label anything that matches the shipped set. Tracks added
            // by hand before this existed are listed as user content, and
            // would otherwise stay mislabelled forever.
            // A bundled title that exists as BOTH .mp3 and .wav means an
            // earlier version installed the compressed copies. Drop the MP3
            // entries so the library doesn't list every track twice - the
            // file itself is left alone, since deleting a user's audio
            // without asking isn't this method's business.
            var superseded = Current.Tracks
                .Where(t => IsBundledTitle(t.DisplayName)
                            && Path.GetExtension(t.FilePath).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                            && Current.Tracks.Any(other =>
                                   IsBundledTitle(other.DisplayName)
                                   && string.Equals(other.DisplayName, t.DisplayName, StringComparison.OrdinalIgnoreCase)
                                   && Path.GetExtension(other.FilePath).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                                   && File.Exists(other.FilePath)))
                .ToList();

            foreach (var track in superseded)
            {
                Current.Tracks.Remove(track);
            }

            // Only re-label a track whose file is actually present. Marking
            // a MISSING file as built-in would hand it to the cleanup below
            // and silently delete it - which matters if someone happens to
            // have named their own track the same as a shipped one.
            foreach (var track in Current.Tracks.Where(t =>
                         !t.IsBuiltIn && IsBundledTitle(t.DisplayName) && File.Exists(t.FilePath)))
            {
                track.IsBuiltIn = true;
            }

            // Built-ins whose file has gone are dropped; a user's own track
            // is left listed even if missing, so a disconnected drive shows
            // as an error they can fix rather than silently vanishing.
            Current.Tracks.RemoveAll(t => t.IsBuiltIn && !File.Exists(t.FilePath));

            if (save)
            {
                Save();
            }
        }
        catch
        {
            // Best-effort - a failed sync must not stop the app opening.
        }
    }

    /// <summary>Turns a filename into something readable: "Neon_Drive__2_" becomes "Neon Drive 2".</summary>
    private static string PrettyTitle(string fileName)
    {
        var cleaned = fileName.Replace('_', ' ').Trim();

        while (cleaned.Contains("  "))
        {
            cleaned = cleaned.Replace("  ", " ");
        }

        return cleaned;
    }

    /// <summary>
    /// The tracks that ship with the app, by title.
    ///
    /// Used to label a track correctly no matter how it arrived - someone
    /// who adds the included set by hand (because the bundled install
    /// didn't reach them) should still see them as "Included" rather than
    /// as their own uploads.
    /// </summary>
    private static readonly HashSet<string> BundledTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Horizon Line", "Chasing Sunset", "Grid Runner", "Afterglow", "Cooldown Lap",
        "Vanishing Point", "Last Lap Lights", "Redline Memory", "One More Run",
        "Back to the Grid", "Long Way Home",

        // The sound packs' own tracks (0.55) - see SoundPacks.PackMusic.
        "Victory Lane", "Insert Coin", "Race Day Live", "Neon Grid",

        // Broadcast's own songs (16 Oct 2026).
        "Broadcast Song 1", "Broadcast Song 2", "Broadcast Song 3", "Broadcast Song 4",

        // Pit Lane's own songs (16 Oct 2026).
        "Pit Lane Song 1", "Pit Lane Song 2", "Pit Lane Song 3", "Pit Lane Song 4",

        // Classic's own songs (16 Oct 2026).
        "Classic Song 1", "Classic Song 2", "Classic Song 3", "Classic Song 4",
    };

    public static bool IsBundledTitle(string title) => BundledTitles.Contains(title.Trim());

    /// <summary>
    /// Maps the soundtrack's ORIGINAL filenames to their release titles.
    ///
    /// The shipped tracks were authored under working names, and someone
    /// adding them from their own copy of the source files would otherwise
    /// end up with "Neon Drive  2" in the library instead of "Grid Runner".
    /// Matching on the filename means those come in correctly named without
    /// anyone having to rename anything by hand.
    /// </summary>
    private static readonly Dictionary<string, string> SourceNameToTitle = new(StringComparer.OrdinalIgnoreCase)
    {
        // Both naming conventions: the underscore form as authored, and the
        // spaced "(1)" form a browser produces when the same base name is
        // downloaded more than once. A user's Downloads folder has the
        // second kind, so matching only the first found nothing.
        ["Neon_Drive"] = "Horizon Line",
        ["Neon Drive"] = "Horizon Line",
        ["Neon_Drive__1_"] = "Chasing Sunset",
        ["Neon Drive (1)"] = "Chasing Sunset",
        ["Neon_Drive__2_"] = "Grid Runner",
        ["Neon Drive (2)"] = "Grid Runner",

        ["Neon_Ballad"] = "Afterglow",
        ["Neon Ballad"] = "Afterglow",
        ["Neon_Ballad__1_"] = "Cooldown Lap",
        ["Neon Ballad (1)"] = "Cooldown Lap",

        ["Neon_Horizon"] = "Vanishing Point",
        ["Neon Horizon"] = "Vanishing Point",
        ["Neon_Horizon__1_"] = "Last Lap Lights",
        ["Neon Horizon (1)"] = "Last Lap Lights",

        ["Send_Me_Back"] = "Redline Memory",
        ["Send Me Back"] = "Redline Memory",
        ["Send_Me_Back__1_"] = "One More Run",
        ["Send Me Back (1)"] = "One More Run",
        ["Send_Me_Back__2_"] = "Back to the Grid",
        ["Send Me Back (2)"] = "Back to the Grid",
        ["Send_Me_Back__3_"] = "Long Way Home",
        ["Send Me Back (3)"] = "Long Way Home",
    };

    /// <summary>
    /// The title to use for a file, preferring a known soundtrack mapping
    /// and otherwise tidying the filename.
    ///
    /// Deliberately does NOT strip a trailing "(2)" before matching. That
    /// looked like a sensible way to handle duplicate downloads, but for
    /// this soundtrack the number is part of the identity - "Neon Drive
    /// (1)" and "Neon Drive (2)" are different tracks, and stripping it
    /// collapsed three of them onto the same title.
    /// </summary>
    public static string TitleForFile(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);

        return SourceNameToTitle.TryGetValue(stem, out var mapped)
            ? mapped
            : PrettyTitle(stem);
    }

    public static bool IsSupportedAudio(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Copies a track into the app's music folder and lists it. Returns the
    /// new track, or null if the copy failed.
    /// </summary>
    public MusicTrack? AddTrack(string sourcePath)
    {
        try
        {
            Directory.CreateDirectory(MusicDirectory);

            var target = Path.Combine(MusicDirectory, Path.GetFileName(sourcePath));

            // Don't clobber an existing file of the same name - suffix it.
            var counter = 2;
            while (File.Exists(target) && !string.Equals(target, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(sourcePath);
                target = Path.Combine(MusicDirectory, $"{name} ({counter}){Path.GetExtension(sourcePath)}");
                counter++;
            }

            if (!string.Equals(target, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, target);
            }

            var track = new MusicTrack
            {
                Title = TitleForFile(target),
                FilePath = target,
                // Matched by title, so the included set is labelled right
                // even when added by hand.
                IsBuiltIn = IsBundledTitle(TitleForFile(target)),
            };

            Current.Tracks.Add(track);
            Save();
            return track;
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and this
        // library was built on the defaults it loaded as. The change still
        // applies this session. The console has already said why.
        if (LoadedUnreadable)
        {
            return;
        }

        try
        {
            // FilePath is written as the full path, which is what every
            // version of the app reads, and the music folder's own tracks
            // also get their path inside it in a field of their own (see
            // MusicFolderPathField), which is what lets a restored library
            // find them on another PC. Added in the JSON rather than on a
            // copy of the settings, so a setting added to MusicSettings
            // later is saved without anyone having to remember this method.
            var document = JsonSerializer.SerializeToNode(Current);
            if (document?[nameof(MusicSettings.Tracks)] is JsonArray tracks)
            {
                foreach (var track in tracks)
                {
                    if (track is JsonObject entry
                        && entry[nameof(MusicTrack.FilePath)] is JsonValue value
                        && value.TryGetValue<string>(out var path)
                        && ToStoredPath(path) is { } inMusicFolder)
                    {
                        entry[MusicFolderPathField] = inMusicFolder;
                    }
                }
            }

            // SafeFile rather than a bare write, like the other stores: an
            // atomic swap, so a crash mid-save can't truncate the library,
            // and it honours the freeze after a restore.
            if (document is not null)
            {
                SafeFile.WriteAllText(FilePath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            // Best-effort, consistent with the other stores.
        }
    }

    /// <summary>
    /// Settles PlayThemeMusic for a file written before it existed. Runs
    /// once: after this the choice is saved and it is the club's.
    ///
    /// Theme music when a soundscape with music of its own was applied,
    /// because that is what those clubs were hearing - applying one switched
    /// every other track off in the playlist. Their own music otherwise.
    ///
    /// That old apply also wrote over the club's own ticks. When the ticks
    /// are exactly the soundscape's tracks and nothing else, they are the
    /// soundscape's doing rather than the club's, so every track is ticked
    /// again - a new track's default - and "My music" plays the whole
    /// library, as it did before any soundscape was applied. A playlist the
    /// club picked by hand never matches that exactly, and is left alone.
    /// </summary>
    public void SettleMusicSource(ThemeMusicSource? theme)
    {
        if (Current.PlayThemeMusic is not null)
        {
            return;
        }

        var themeTracks = theme?.TracksIn(Current.Tracks) ?? new List<MusicTrack>();
        Current.PlayThemeMusic = themeTracks.Count > 0;

        if (themeTracks.Count > 0)
        {
            var themeIds = themeTracks.Select(t => t.Id).ToHashSet();
            var enabledIds = Current.Tracks.Where(t => t.Enabled).Select(t => t.Id).ToHashSet();

            if (enabledIds.SetEquals(themeIds) && Current.Tracks.Count > themeIds.Count)
            {
                foreach (var track in Current.Tracks)
                {
                    track.Enabled = true;
                }
            }
        }

        // Not over a file that couldn't be read - the same rule as Load's
        // own sync. The choice still applies for this session.
        if (!LoadedUnreadable)
        {
            Save();
        }
    }

    /// <summary>
    /// Raised after an undo has put tracks back in (or taken them out of) the
    /// library, so a page showing it re-reads it. Not raised by ordinary saves.
    /// </summary>
    public event Action? Restored;

    /// <summary>Tells whatever shows this store that an undo just changed it. UI thread.</summary>
    public void NotifyRestored() => Restored?.Invoke();

    /// <summary>
    /// Puts removed tracks back, each at the place it was taken from, for
    /// Undo. A track already listed again by the time the undo runs (by id,
    /// or by the same file - the start-up sync re-lists anything left in the
    /// music folder) is left as it is rather than listed twice. Saves.
    /// </summary>
    public void PutBack(IEnumerable<(int Index, MusicTrack Track)> removed)
    {
        foreach (var (index, track) in removed.OrderBy(r => r.Index))
        {
            if (Current.Tracks.Any(t => t.Id == track.Id
                                        || (!string.IsNullOrEmpty(track.FilePath)
                                            && string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            Current.Tracks.Insert(Math.Clamp(index, 0, Current.Tracks.Count), track);
        }

        Save();
    }

    public string Snapshot() => JsonSerializer.Serialize(Current);

    public void Restore(string snapshot)
    {
        try
        {
            var restored = JsonSerializer.Deserialize<MusicSettings>(snapshot);
            if (restored is not null)
            {
                // Undo is for the library (add, remove, clear). The Theme
                // music / My music choice isn't part of that, and the Music
                // page's radios would no longer match a reverted one.
                restored.PlayThemeMusic = Current.PlayThemeMusic ?? restored.PlayThemeMusic;
                Current = restored;
                Save();
            }
        }
        catch
        {
            // A failed restore leaves the current state alone.
        }
    }
}
