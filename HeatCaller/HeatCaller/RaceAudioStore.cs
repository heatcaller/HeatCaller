using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Whether the night is recorded, where the files go, and the rails that
/// stop a recording ever being the thing that ruins a race night.
///
/// Its own store rather than a few more members on AppSettings. Two reasons,
/// and the second is the one that matters: settings.json is read and written
/// by nearly every page, and a club that turns recording off wants that to
/// be one small file it can also just delete; and the folder below decides
/// where hundreds of megabytes a season land, which is a setting worth being
/// able to look at on its own.
///
/// The folder is deliberately NOT in the data folder (AppPaths.Root).
/// Backups zip that folder whole, and the fifth bug hunt found photo-finish
/// frames being copied into every one of fourteen kept backups until they
/// filled a laptop. An evening of race audio is several times the size of a
/// night of photo-finish frames, so it is kept where a backup can never
/// reach it in the first place - beside the backups themselves, in
/// Documents, the same place BackupService.DefaultBackupFolder uses and for
/// the same reason. Nothing here is club data in the sense a backup means:
/// it is a recording of a broadcast, and a club that wants it kept copies
/// that folder, exactly as it is told to do with the photo-finish frames.
/// </summary>
internal sealed class RaceAudioStore
{
    private static string FilePath => Path.Combine(AppPaths.Root, "race-audio.json");

    /// <summary>
    /// Where the recordings go when the club has not said otherwise:
    /// Documents\HeatCaller Race Audio.
    ///
    /// Through Rehearsal.DocumentsFolder, so the rehearsal copy writes into
    /// its own folder and can never drop a night of audio among the club's
    /// real recordings - the same rule the backups follow.
    /// </summary>
    public static string DefaultFolder => Rehearsal.DocumentsFolder("HeatCaller Race Audio");

    /// <summary>
    /// The longest one recording may run before it stops itself.
    ///
    /// A heat is five to ten minutes. This is for the night where a race is
    /// staged and the director is called away, or a race is left in the
    /// Racing state and never finished: without it, the file would grow for
    /// as long as the app was open. At the stored size (mono 16-bit 44.1kHz,
    /// about 5MB a minute) the default is a quarter of a gigabyte, which is
    /// a lot and is also a ceiling, which is the point.
    /// </summary>
    private const int DefaultMaxMinutes = 40;

    /// <summary>
    /// How much room must be left on the disk for a recording to start, and
    /// to carry on.
    ///
    /// The race itself needs the disk: the crash journal is written after
    /// every crossing and the race history after every race, and RaceFiling
    /// says what a failed history write costs. A recording is the one thing
    /// here that is nice to have, so it is the first thing to get out of the
    /// way when the disk is running out.
    /// </summary>
    private const int DefaultMinFreeMb = 750;

    /// <summary>
    /// How many race nights' recordings are kept before the oldest night's
    /// folder is deleted.
    ///
    /// SIX, which is about six weeks at a weekly club and about a quarter
    /// of a gigabyte a night at the stored size - a gigabyte and a half,
    /// which is a lot and is also a ceiling.
    ///
    /// A CEILING RATHER THAN AN ARCHIVE, and the difference is the whole
    /// reason this exists. The feature wrote an mp3 of every race and a
    /// clip for every driver in it, and nothing ever deleted any of it: a
    /// club running a season would have put twenty gigabytes of broadcast
    /// into Documents without ever being told it was happening, which is
    /// the exact fault the fifth bug hunt found in the photo-finish frames.
    /// A club that wants a night kept copies the folder, which is what the
    /// class note already tells them to do.
    /// </summary>
    private const int DefaultKeepNights = 6;

    private sealed class AudioFile
    {
        public bool Enabled { get; set; } = true;
        public bool DriverHighlights { get; set; } = true;

        /// <summary>Which drivers get a highlight video - see HighlightVideo.Who. Every driver by default; only the podium ever goes online.</summary>
        public int HighlightVideos { get; set; } = (int)HighlightVideo.Who.Everyone;

        /// <summary>The club's social handle for the end of each video, or empty.</summary>
        public string VideoHandle { get; set; } = string.Empty;

        /// <summary>Whether the podium's videos go on the club's page, with a QR code on the Big Screen. On by default.</summary>
        public bool SharePodiumVideos { get; set; } = true;

        /// <summary>Whether each driver's own race clips go up to their private night page. Off by default.</summary>
        public bool ShareDriverClips { get; set; }

        public string Folder { get; set; } = string.Empty;
        public int MaxMinutes { get; set; } = DefaultMaxMinutes;
        public int MinFreeMb { get; set; } = DefaultMinFreeMb;
        public int KeepNights { get; set; } = DefaultKeepNights;
    }

    private readonly AudioFile _file;

    private RaceAudioStore(AudioFile file) => _file = file;

    /// <summary>
    /// True when the file was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. Nothing is saved over it this
    /// session, for the reason LapBeepStore gives: a file locked for a
    /// moment at launch must not be replaced by the defaults.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    /// <summary>
    /// Switched off for the whole test run - set by TestSetup, exactly as
    /// AudioBus.Silent is, and for a sharper reason than silence.
    ///
    /// The recordings deliberately go to Documents rather than into the app
    /// data folder, so that backups cannot possibly zip them (see Folder).
    /// But AppPaths.RedirectForTests only redirects the app data folder - its
    /// own note says Documents is not covered, on purpose - so every test
    /// that drives a race to the green would otherwise record a real wav into
    /// the real Documents\HeatCaller Race Audio on whoever's machine ran the
    /// suite. RaceNightTests plays a whole race night through.
    ///
    /// Checked in Load rather than at the call sites so there is one place it
    /// can be true and no way to drive a recording round it.
    /// </summary>
    internal static bool OffForTests { get; set; }

    public static RaceAudioStore Load()
    {
        if (OffForTests)
        {
            return new RaceAudioStore(new AudioFile { Enabled = false, DriverHighlights = false });
        }

        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var file = JsonSerializer.Deserialize<AudioFile>(SafeFile.ReadAllTextWithRetry(FilePath));

                if (file is not null)
                {
                    return new RaceAudioStore(file);
                }
            }
        }
        catch (Exception ex)
        {
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new RaceAudioStore(new AudioFile()) { LoadedUnreadable = unreadable };
    }

    public void Save()
    {
        if (LoadedUnreadable)
        {
            return;
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(
            _file,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// The master switch. On by default - the owner asked for the night to
    /// be recorded - and off is one tick box, or deleting race-audio.json
    /// after setting this to false in it.
    ///
    /// Turning it off mid-night does not stop a recording already running:
    /// that one finishes and is written out, because half a race on disk
    /// with no mp3 beside it is worse than the file the club has just said
    /// it does not want. It stops the next one starting.
    /// </summary>
    public bool Enabled
    {
        get => _file.Enabled;
        set
        {
            if (_file.Enabled == value)
            {
                return;
            }

            _file.Enabled = value;
            Save();
        }
    }

    /// <summary>Whether a clip is cut for each driver as well as the race's own mp3. Nothing is cut when the race's recording was too short to be worth anything.</summary>
    public bool DriverHighlights
    {
        get => _file.DriverHighlights;
        set
        {
            if (_file.DriverHighlights == value)
            {
                return;
            }

            _file.DriverHighlights = value;
            Save();
        }
    }

    /// <summary>
    /// Which drivers get a shareable highlight video after each race
    /// (21 Oct 2026) - see HighlightVideo. Made from their clip, so only when
    /// <see cref="DriverHighlights"/> is on as well.
    /// </summary>
    public HighlightVideo.Who HighlightVideos
    {
        get => Enum.IsDefined(typeof(HighlightVideo.Who), _file.HighlightVideos)
            ? (HighlightVideo.Who)_file.HighlightVideos
            : HighlightVideo.Who.Winner;
        set
        {
            if (_file.HighlightVideos == (int)value)
            {
                return;
            }

            _file.HighlightVideos = (int)value;
            Save();
        }
    }

    /// <summary>
    /// Whether the top three's videos are put on the club's page for the
    /// drivers to download, with a QR code on the Big Screen after the race
    /// (21 Oct 2026). Only ever the podium - see RelayPublisher.Videos.
    /// </summary>
    public bool SharePodiumVideos
    {
        get => _file.SharePodiumVideos;
        set
        {
            if (_file.SharePodiumVideos == value)
            {
                return;
            }

            _file.SharePodiumVideos = value;
            Save();
        }
    }

    /// <summary>
    /// Whether each driver's own race clips (the mp3 of their race) go up to
    /// their private night page on the relay, reachable only with their
    /// badge (22 Oct 2026, driver experience). OFF by default: it is a
    /// recording of a named person, so a club decides, not an update. Also
    /// needs driver nights on.
    /// </summary>
    public bool ShareDriverClips
    {
        get => _file.ShareDriverClips;
        set
        {
            if (_file.ShareDriverClips == value)
            {
                return;
            }

            _file.ShareDriverClips = value;
            Save();
        }
    }

    /// <summary>The club's social handle, shown at the end of each highlight video - "@yourclub". Empty for none.</summary>
    public string VideoHandle
    {
        get => (_file.VideoHandle ?? string.Empty).Trim();
        set
        {
            var handle = (value ?? string.Empty).Trim();

            if (string.Equals(_file.VideoHandle, handle, StringComparison.Ordinal))
            {
                return;
            }

            _file.VideoHandle = handle;
            Save();
        }
    }

    /// <summary>
    /// Where the recordings go: the stored folder when it is a real local
    /// one, and <see cref="DefaultFolder"/> otherwise.
    ///
    /// A network path is refused outright, and that is the whole point.
    /// BackupService.SafeBackupFolder has the long version: merely asking
    /// Windows whether \\somewhere\share exists makes it log on to that
    /// machine with this account, so a path out of a restored or
    /// hand-edited file could hand a password hash to whoever runs the
    /// server without anything being recorded at all. A relative path is
    /// refused too - it would land wherever the app happens to be running
    /// from, which for an installed build is Program Files.
    /// </summary>
    public string Folder
    {
        get
        {
            var configured = _file.Folder;

            // WHY (23 Oct 2026, bug hunt 10): a rehearsal copies
            // race-audio.json as it is, so a club's own folder (D:\Race
            // Audio) came along with it. The cold open, sign-off, recap and
            // driver pieces then made and marked tonight's night in the
            // club's REAL folder, and the prune after them deleted the
            // club's oldest real night to make room. Now a rehearsal always
            // records into its own folder, as BackupService.SafeBackupFolder
            // does for backups - a folder inside that one is kept. The
            // stored setting is untouched (FolderSetting), and the Sounds >
            // Recording page shows this folder, the one really used.
            if (AppPaths.IsRehearsal)
            {
                try
                {
                    return !string.IsNullOrWhiteSpace(configured)
                        && !PlainFileName.IsNetworkPath(configured)
                        && Path.IsPathFullyQualified(configured)
                        && PlainFileName.IsInside(DefaultFolder, configured)
                            ? configured
                            : DefaultFolder;
                }
                catch (ArgumentException)
                {
                    return DefaultFolder;
                }
            }

            if (string.IsNullOrWhiteSpace(configured) || PlainFileName.IsNetworkPath(configured))
            {
                return DefaultFolder;
            }

            try
            {
                return Path.IsPathFullyQualified(configured) ? configured : DefaultFolder;
            }
            catch (ArgumentException)
            {
                // Characters Windows will not have in a path at all.
                return DefaultFolder;
            }
        }
    }

    /// <summary>What the file actually says, before <see cref="Folder"/> has its say - for a settings page to show and set.</summary>
    public string FolderSetting
    {
        get => _file.Folder;
        set
        {
            // Blank means "wherever the default is", which is what an empty
            // box on a settings page means too.
            var wanted = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

            if (string.Equals(_file.Folder, wanted, StringComparison.Ordinal))
            {
                return;
            }

            _file.Folder = wanted;
            Save();
        }
    }

    /// <summary>The length cap - see <see cref="DefaultMaxMinutes"/>. Floored at one minute and capped at three hours, so a hand-edited file cannot remove the ceiling.</summary>
    public int MaxMinutes => Math.Clamp(_file.MaxMinutes <= 0 ? DefaultMaxMinutes : _file.MaxMinutes, 1, 180);

    /// <summary>The free-disk floor in MB - see <see cref="DefaultMinFreeMb"/>. Never below 100MB, whatever the file says.</summary>
    public int MinFreeMb => Math.Max(100, _file.MinFreeMb <= 0 ? DefaultMinFreeMb : _file.MinFreeMb);

    /// <summary>
    /// How many nights are kept - see <see cref="DefaultKeepNights"/>.
    ///
    /// Floored at one and capped at a hundred and four, which is two years
    /// of weekly racing: a hand-edited file cannot turn the ceiling off,
    /// and it cannot set it to nothing either. Zero would mean tonight's
    /// own folder being deleted the moment the first race was written,
    /// which is a feature that quietly does nothing.
    /// </summary>
    public int KeepNights
    {
        get => Math.Clamp(_file.KeepNights <= 0 ? DefaultKeepNights : _file.KeepNights, 1, 104);
        set
        {
            var wanted = Math.Clamp(value, 1, 104);

            if (_file.KeepNights == wanted)
            {
                return;
            }

            _file.KeepNights = wanted;
            Save();
        }
    }

    /// <summary>
    /// Deletes the oldest night folders until only <see cref="KeepNights"/>
    /// are left, and says what it removed.
    ///
    /// WHY HERE AND NOT IN THE RECORDER. The folder is this store's, the
    /// rule is this store's, and the recorder's job is to get one race onto
    /// the disk. It is called after a race has been written, which is both
    /// the only moment the folder definitely grew and a moment nothing is
    /// waiting on - never on the audio path, never at launch.
    ///
    /// BY NAME, NOT BY TIMESTAMP. Every night folder is named yyyy-MM-dd
    /// (FolderForNight), and a name sorts the same way a date does. A
    /// folder's own modified time is not the night it holds: copying the
    /// folder, a sync tool touching it, or restoring it from a backup all
    /// move it, and a club would lose the wrong night.
    ///
    /// ANYTHING THAT IS NOT A NIGHT IS LEFT ALONE. A club that drops a
    /// folder called "Keep these" in there keeps it, which is exactly what
    /// somebody who has just read the note about copying a night would do.
    ///
    /// AND A DATE ON ITS OWN DOES NOT MAKE A NIGHT OURS (13 Oct 2026). A
    /// club can point the recordings at any folder - Pictures, say, where
    /// the photo import files everything under yyyy-MM-dd - and this
    /// deletes whole folders. So outside the default folder, which only
    /// HeatCaller ever writes into, a night counts only when it carries the
    /// marker StartFor leaves in it (<see cref="MarkAsNight"/>).
    ///
    /// Never throws. A folder that will not delete - a file open in a media
    /// player, a sync tool holding it - is reported and skipped, and tried
    /// again after the next race.
    /// </summary>
    public void PruneOldNights(Action<string>? log = null)
    {
        try
        {
            var root = Folder;

            if (!Directory.Exists(root))
            {
                return;
            }

            var ownFolder = IsDefaultFolder(root);

            // Only the folders that ARE nights, newest name first.
            //
            // "Folder" and not "Path" as the element name: a tuple element
            // called Path inside a file that also calls System.IO.Path by
            // name is a compiler error waiting for whoever writes the next
            // line of it.
            var nights = Directory.EnumerateDirectories(root)
                .Select(folder => (Folder: folder, Name: System.IO.Path.GetFileName(folder)))
                .Where(one => IsNightFolderName(one.Name))
                .Where(one => ownFolder || File.Exists(System.IO.Path.Combine(one.Folder, NightMarkerFileName)))
                .OrderByDescending(one => one.Name, StringComparer.Ordinal)
                .Skip(KeepNights)
                .ToList();

            if (nights.Count == 0)
            {
                return;
            }

            var gone = 0;

            foreach (var (folder, name) in nights)
            {
                try
                {
                    // ONLY OUR OWN FILES OUTSIDE THE DEFAULT FOLDER (17 Oct
                    // 2026, Bug Hunt 8, A14-1). A marked night in a club's
                    // own folder can still hold the club's files: the photo
                    // import files tonight's pictures under the same
                    // yyyy-MM-dd the first race made. So there the
                    // recordings, the clips and the marker go, and the
                    // folder only if that leaves it empty.
                    if (ownFolder)
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                    else
                    {
                        DeleteOurOwnFrom(folder);
                    }

                    gone++;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"{AppTime.LogStampNow()}  [audio] Couldn't remove the recordings from {name} "
                                + $"({ex.Message}). They will be tried again after the next race.");
                }
            }

            if (gone > 0)
            {
                log?.Invoke($"{AppTime.LogStampNow()}  [audio] Removed the recordings from "
                            + $"{Plural.Of(gone, "night")}, keeping the last {Plural.Of(KeepNights, "night")}. "
                            + "Copy a night's folder somewhere else to keep it.");
            }
        }
        catch (Exception ex)
        {
            // A folder that cannot be listed costs the club some disk and
            // never a race.
            log?.Invoke($"{AppTime.LogStampNow()}  [audio] Couldn't tidy the recordings folder ({ex.Message}).");
        }
    }

    /// <summary>
    /// Deletes what HeatCaller writes into a night folder - the race and
    /// piece recordings (mp3, wav, and an in-progress wav), the Highlights
    /// clips, and the marker - and then the folder, only if nothing else is
    /// left in it. 17 Oct 2026, Bug Hunt 8, A14-1.
    ///
    /// The marker goes LAST and only once every recording has gone, so a
    /// file held open by a media player leaves the night marked and it is
    /// tried again after the next race, as the recursive delete was.
    /// Throws on a file that will not delete; the caller reports it.
    /// </summary>
    internal static void DeleteOurOwnFrom(string nightFolder)
    {
        static void DeleteRecordingsIn(string folder, bool videosToo)
        {
            foreach (var file in Directory.EnumerateFiles(folder).ToList())
            {
                var extension = System.IO.Path.GetExtension(file);

                // .mp4 too (22 Oct 2026, bug hunt): the highlight videos
                // live here as well, and were left behind for good.
                //
                // WHY (23 Oct 2026, bug hunt 10): but only in Highlights,
                // the one place HeatCaller writes a video. At the night
                // folder's top level an .mp4 is the club's own - a photo
                // import files a phone's videos into the same yyyy-MM-dd
                // folder - and it was deleted for good with the recordings.
                if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase)
                    || (videosToo && string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase)))
                {
                    File.Delete(file);
                }
            }
        }

        DeleteRecordingsIn(nightFolder, videosToo: false);

        var highlights = System.IO.Path.Combine(nightFolder, DriversOwnClip.FolderName);

        if (Directory.Exists(highlights))
        {
            DeleteRecordingsIn(highlights, videosToo: true);

            if (!Directory.EnumerateFileSystemEntries(highlights).Any())
            {
                Directory.Delete(highlights);
            }
        }

        var marker = System.IO.Path.Combine(nightFolder, NightMarkerFileName);

        // Only once the Highlights folder has gone (22 Oct 2026, bug hunt):
        // with the marker taken while anything of ours was left there, the
        // folder stopped counting as a night and was never tried again.
        if (File.Exists(marker) && !Directory.Exists(highlights))
        {
            File.Delete(marker);
        }

        if (!Directory.EnumerateFileSystemEntries(nightFolder).Any())
        {
            Directory.Delete(nightFolder);
        }
    }

    /// <summary>
    /// Whether a folder name is one of ours: exactly yyyy-MM-dd, which is
    /// what <see cref="FolderForNight"/> writes.
    ///
    /// Parsed rather than pattern-matched on length, so "2026-13-45" is not
    /// a night and is left alone with everything else a club has put in
    /// there.
    /// </summary>
    private static bool IsNightFolderName(string? name) =>
        name is { Length: 10 }
        && DateTime.TryParseExact(
            name,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _);

    /// <summary>
    /// The file StartFor leaves in each night folder it makes, so the prune
    /// can tell a night HeatCaller made from a club's own dated folder.
    /// </summary>
    internal const string NightMarkerFileName = ".heatcaller-night";

    /// <summary>
    /// Marks a night folder as one HeatCaller made - see PruneOldNights.
    ///
    /// Never throws. A night left unmarked is only ever kept for longer
    /// than it should be, never deleted that should not be.
    /// </summary>
    public static void MarkAsNight(string nightFolder)
    {
        try
        {
            var marker = System.IO.Path.Combine(nightFolder, NightMarkerFileName);

            if (!File.Exists(marker))
            {
                File.WriteAllText(marker, "HeatCaller made this folder for one race night's recordings. It is "
                    + "deleted with everything in it once it is older than the nights Sounds > Recording keeps.");
            }
        }
        catch
        {
            // See above.
        }
    }

    /// <summary>
    /// Whether a folder is the default one, which only HeatCaller writes
    /// into - so its nights from before the marker existed still count.
    /// </summary>
    private static bool IsDefaultFolder(string root)
    {
        try
        {
            return string.Equals(
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root)),
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(DefaultFolder)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// How much is in the recordings folder, in megabytes, and how many
    /// nights - for a settings page to show, because a club cannot decide
    /// how many nights to keep without knowing what a night costs.
    ///
    /// Zero and zero when the folder is not there yet, which is every club
    /// that has not finished a race since installing. Never throws.
    /// </summary>
    public (int Nights, long Megabytes) OnDisk()
    {
        try
        {
            var root = Folder;

            if (!Directory.Exists(root))
            {
                return (0, 0);
            }

            var nights = Directory.EnumerateDirectories(root).Count(one => IsNightFolderName(System.IO.Path.GetFileName(one)));

            var bytes = 0L;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
                }
                catch
                {
                    // A file deleted between the listing and the measuring.
                }
            }

            return (nights, bytes / (1024 * 1024));
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// The folder tonight's files go in: the audio folder, then the night.
    ///
    /// A folder per night rather than one flat heap, because a club looking
    /// for "that race three weeks ago" looks for the date first and there
    /// are twenty files a night. The date is in each file's name as well -
    /// see RaceHighlights.BaseNameFor - so a file dragged out of here on its
    /// own still says when it is from.
    ///
    /// By the night it was rather than the calendar date, and in the
    /// invariant culture - the same as PhotoFinish.FolderFor. A meeting that
    /// runs past midnight is one night: split in two, the prune counted it
    /// twice, and with one night kept it deleted the evening's earlier
    /// races after the first race past midnight.
    /// </summary>
    public string FolderForNight(DateTime localStart) =>
        Path.Combine(Folder, TonightAtTheTrack.NightOf(localStart).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
}
