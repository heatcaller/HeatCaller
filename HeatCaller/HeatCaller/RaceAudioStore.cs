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

    private sealed class AudioFile
    {
        public bool Enabled { get; set; } = true;
        public bool DriverHighlights { get; set; } = true;
        public string Folder { get; set; } = string.Empty;
        public int MaxMinutes { get; set; } = DefaultMaxMinutes;
        public int MinFreeMb { get; set; } = DefaultMinFreeMb;
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
    /// The folder tonight's files go in: the audio folder, then the night.
    ///
    /// A folder per night rather than one flat heap, because a club looking
    /// for "that race three weeks ago" looks for the date first and there
    /// are twenty files a night. The date is in each file's name as well -
    /// see RaceHighlights.BaseNameFor - so a file dragged out of here on its
    /// own still says when it is from.
    /// </summary>
    public string FolderForNight(DateTime localStart) =>
        Path.Combine(Folder, localStart.ToString("yyyy-MM-dd"));
}
