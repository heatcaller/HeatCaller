using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// Backs up and restores everything the app persists: driver profiles, race
/// classes/rounds, session history, track layouts, sound effects, and voice
/// settings all live as separate JSON files under one folder
/// (%AppData%\HeatCaller), so a full backup is just a zip of that
/// whole folder, and restore is extracting one back over it.
///
/// The one addition is secrets.hgk: the keys, tokens and PINs, locked with
/// the club's backup password when one is set. The files themselves only
/// hold those sealed to this Windows account, so without it a backup
/// restored on another PC comes back without them (see SecretRecovery).
///
/// Backups default to a Documents subfolder rather than AppData itself,
/// since AppData is hidden from casual browsing by default and the whole
/// point of a backup is being able to find and copy it off the machine.
/// </summary>
public static class BackupService
{
    internal static string DataDirectory => AppPaths.Root;

    /// <summary>
    /// The backup's copy of the keys, tokens and PINs, locked with the
    /// club's backup password (see SecretBox.PasswordSeal). Sits at the top
    /// of the zip, so a restore puts it at the top of the data folder,
    /// where SecretRecovery picks it up on the next launch.
    /// </summary>
    internal const string SecretsFileName = "secrets.hgk";

    /// <summary>
    /// Hands over the secrets to put in a backup, with the password to lock
    /// them with - or null when there is no backup password, in which case
    /// backups carry no secrets at all. Set once by MainForm at startup; a
    /// function rather than a value so every backup takes them as they are
    /// at that moment.
    ///
    /// Everything else in the data folder is already safe to zip: the
    /// settings and voice files hold their secrets sealed to this Windows
    /// account, which is useless on any other PC - hence this separate,
    /// password-locked copy.
    /// </summary>
    internal static Func<BackupSecrets?>? SecretsProvider { get; set; }

    public static string DefaultBackupFolder => Rehearsal.DocumentsFolder("HeatCaller Backups");

    /// <summary>
    /// Where backups actually go, given whatever the Backups page has saved:
    /// that folder when it is a real local one, and the default otherwise.
    ///
    /// The one rule, in one place, because there are four readers of that
    /// setting - this class, the Backups page, the automatic pass in MainForm
    /// and the startup migration, which reads settings.json directly before
    /// there is an AppSettings at all.
    ///
    /// A UNC path is refused, and that is the whole point. AutoBackupFolder is
    /// an ORDINARY setting: it is not sealed, and a restored settings.json can
    /// say anything. Pointed at \\somewhere\share it made every automatic
    /// backup - the entire data folder, driver names, emails, photos,
    /// check-ins, session history, and the password-locked secrets file - post
    /// itself daily to a machine the club has never heard of, with nothing on
    /// screen. Worse, Directory.CreateDirectory on that path makes Windows
    /// authenticate to it with the logged-in account, so the startup migration
    /// leaked an NTLM hash there before the Backups page had ever been opened.
    ///
    /// A mapped drive letter is deliberately still allowed. A club backing up
    /// to a NAS on Z: is a real setup, and a planted value naming Z: only
    /// resolves if this machine already has Z: mapped - which the writer of
    /// the file cannot arrange. UNC is the only shape that lets a restored
    /// value name a destination of its own choosing.
    ///
    /// The same reasoning PlainFileName.IsNetworkPath was written for; this is
    /// the one planted-path site that never called it.
    /// </summary>
    public static string SafeBackupFolder(string? configured)
    {
        // A rehearsal copy's backups stay in the rehearsal's own folder,
        // whatever its copied settings say. In the club's folder they would
        // sit among the real ones - and automatic-backup pruning, which
        // keeps only the newest few, would delete the club's real backups
        // to make room for the rehearsal's.
        if (AppPaths.IsRehearsal)
        {
            return DefaultBackupFolder;
        }

        if (string.IsNullOrWhiteSpace(configured) || PlainFileName.IsNetworkPath(configured))
        {
            return DefaultBackupFolder;
        }

        try
        {
            // Rooted and absolute. A relative path would land wherever the app
            // happens to be running from, which for an installed build is
            // Program Files.
            return Path.IsPathFullyQualified(configured) ? configured : DefaultBackupFolder;
        }
        catch (ArgumentException)
        {
            // Characters Windows will not accept in a path at all.
            return DefaultBackupFolder;
        }
    }

    /// <summary>
    /// The race journal's file name - the race in progress, saved as it runs
    /// so a crash can be recovered from. Must match RaceJournal.FilePath,
    /// which is private to it.
    /// </summary>
    private const string RaceJournalFileName = "race-in-progress.json";

    private static string PreRestoreSafetyFolder(string backupFolder) => Path.Combine(backupFolder, "Pre-Restore Safety Backups");

    // Every backup this app writes starts with one of these. Listing and
    // pruning only ever look at files that do: the backup folder is one
    // the user picks, and it can just as well be Documents or Downloads,
    // full of zips that are nothing to do with this app.
    private const string ManualPrefix = "HeatCaller-Backup-";

    // Automatic backups get their own prefix so that pruning - which only
    // exists to stop the automatic ones piling up - never deletes a backup
    // someone made on purpose.
    private const string AutomaticPrefix = "HeatCaller-AutoBackup-";

    private const string StampFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>
    /// Zips the entire app data folder into a timestamped .zip in the given
    /// folder (or the default backup folder), and returns the full path to
    /// the created file. <paramref name="automatic"/> marks it as one the
    /// scheduled backup made, which is the only kind pruning removes.
    ///
    /// Takes the secrets from <see cref="SecretsProvider"/>, which reads the
    /// live settings - so call this one on the UI thread. A backup made in
    /// the background takes them first, on the UI thread, with
    /// <see cref="CaptureSecrets"/>, and passes them to the overload below.
    /// </summary>
    public static string CreateBackup(string? destinationFolder = null, bool automatic = false) =>
        CreateBackup(destinationFolder, automatic, CaptureSecrets());

    /// <summary>
    /// The secrets as they stand, for a backup about to be made - or null
    /// when there is no backup password. Call on the UI thread: it reads
    /// the live settings, which the UI thread is free to be changing.
    /// </summary>
    internal static BackupSecrets? CaptureSecrets() => SecretsProvider?.Invoke();

    /// <summary>
    /// Makes a backup with secrets already taken by <see cref="CaptureSecrets"/>.
    ///
    /// Safe to run off the UI thread, and meant to be: zipping the music
    /// library can take long enough to freeze the app mid-race. It touches
    /// nothing in memory - only files - and reads the settings files all at
    /// one moment, with every save held off for the instant that takes (see
    /// SafeFile.WhileNoWritesUnderWay), so the backup never mixes a file
    /// from before a change with one from after it. Locking the secrets
    /// with the backup password is deliberately slow, and happens here too.
    /// </summary>
    internal static string CreateBackup(string? destinationFolder, bool automatic, BackupSecrets? secrets)
    {
        // Counted for the whole of the backup, whichever thread it runs on,
        // so a restore can refuse to start while one is still being written
        // - see IsBackupRunning.
        Interlocked.Increment(ref _backupsRunning);

        try
        {
            return CreateBackupCore(destinationFolder, automatic, secrets);
        }
        finally
        {
            Interlocked.Decrement(ref _backupsRunning);
        }
    }

    /// <summary>How many backups are being written right now, on any thread. See IsBackupRunning.</summary>
    private static int _backupsRunning;

    /// <summary>
    /// True while a backup is being written - the automatic one in the
    /// background, or Back Up Now.
    ///
    /// A restore checks this before it starts. It replaces the files a
    /// backup still being written is reading, so the backup could zip half
    /// the old data and half the restored, and the restore's own restart
    /// would cut the backup off partway through.
    /// </summary>
    public static bool IsBackupRunning => Volatile.Read(ref _backupsRunning) > 0;

    private static string CreateBackupCore(string? destinationFolder, bool automatic, BackupSecrets? secrets)
    {
        // Through the guard here as well as at every call site: this is the
        // one funnel every backup passes through, so a caller added later
        // cannot reintroduce the planted-destination bug by forgetting it.
        var folder = SafeBackupFolder(destinationFolder);
        Directory.CreateDirectory(folder);

        // Only when no other backup is being written: one that is has its
        // own .tmp in this folder.
        if (Volatile.Read(ref _backupsRunning) == 1)
        {
            DeleteLeftovers(folder, ".tmp");
        }

        if (!Directory.Exists(DataDirectory) || !Directory.EnumerateFileSystemEntries(DataDirectory).Any())
        {
            throw new InvalidOperationException("No saved data found to back up yet - nothing has been configured or raced yet.");
        }

        var prefix = automatic ? AutomaticPrefix : ManualPrefix;
        var fileName = $"{prefix}{DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture)}.zip";
        var fullPath = Path.Combine(folder, fileName);

        // Zip to a temp file first and move into place, so a backup that
        // fails partway through (disk full, etc.) never leaves a corrupt,
        // half-written .zip sitting in the backup folder looking valid.
        var tempPath = fullPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        // Locked before the zip is started, so a problem getting at the
        // secrets fails the backup here rather than halfway through.
        var sealedSecrets = SealSecretsForBackup(secrets);

        try
        {
            WriteZip(tempPath, sealedSecrets);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        File.Move(tempPath, fullPath, overwrite: true);

        return fullPath;
    }

    /// <summary>
    /// Zips the data folder the way ZipFile.CreateFromDirectory does -
    /// every file, plus an entry for each empty folder - leaving out any
    /// secrets file lying in it, and adding this backup's own. The undo
    /// holding folder and the photo-finish frames are left out too (see
    /// IsLeftOutOfBackup).
    ///
    /// Built by hand rather than with CreateFromDirectory and then edited,
    /// because editing a zip (ZipArchiveMode.Update) in .NET reads the
    /// whole archive into memory to write it back, and the data folder
    /// holds the music library. A secrets file can be in the data folder
    /// after a restore that hasn't been dealt with yet; it belongs to that
    /// other backup and must never be passed off as this one's.
    /// </summary>
    private static void WriteZip(string zipPath, string? sealedSecrets)
    {
        var root = new DirectoryInfo(DataDirectory);
        var basePath = Path.GetFullPath(root.FullName).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        // The settings files, read first and all at one moment - see
        // ReadSettingsFilesTogether. Everything else is streamed below.
        var settingsFiles = ReadSettingsFilesTogether(root, basePath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // Written as they were read, whatever has happened on disk since.
        foreach (var (settingsEntryName, copy) in settingsFiles)
        {
            var copyEntry = archive.CreateEntry(settingsEntryName, CompressionLevel.Optimal);
            copyEntry.LastWriteTime = ZipSafeTime(copy.LastWrite);

            using var copyTarget = copyEntry.Open();
            copyTarget.Write(copy.Bytes, 0, copy.Bytes.Length);
        }

        foreach (var item in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            // Zip entry names use forward slashes, as CreateFromDirectory's do.
            var entryName = EntryNameFor(item.FullName, basePath);

            // Files set aside for Undo, and photo-finish frames - see
            // IsLeftOutOfBackup.
            if (IsLeftOutOfBackup(entryName))
            {
                continue;
            }

            // Backups are not data. With the Backups page pointed somewhere
            // inside the data folder, every backup used to zip every backup
            // before it, doubling in size each time until the disk filled.
            // Only this app's own backup zips are left
            // out, wherever in the data folder they lie (the backup folder,
            // its Pre-Restore safety subfolder, or anywhere else): the
            // folder the Backups page points at can be one that also holds
            // real data - DriverPhotos, the music library - and leaving out
            // the whole folder would leave that data out of every backup.
            if (item is FileInfo backupZip && BackupPrefix(backupZip) is not null)
            {
                continue;
            }

            if (item is FileInfo)
            {
                if (entryName.Equals(SecretsFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFullPath(item.FullName), Path.GetFullPath(zipPath), StringComparison.OrdinalIgnoreCase)
                    || IsSaveInProgressFile(item.Name)
                    || settingsFiles.ContainsKey(entryName))
                {
                    continue;
                }

                // Not CreateEntryFromFile: that opens the file FileShare.Read,
                // which fails on any file another handle has open for
                // writing - the iLap raw capture log is held open like that
                // for as long as the decoder is connected, so every backup
                // made on a race day failed. Shared read/write here, and
                // opened before the entry is made so a file that vanished
                // since it was listed (a store's temp file swapped in) is
                // simply left out rather than failing the backup.
                FileStream source;
                try
                {
                    source = OpenShared(item.FullName);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    continue;
                }

                using (source)
                {
                    var fileEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    fileEntry.LastWriteTime = ZipSafeTime(File.GetLastWriteTime(item.FullName));

                    // Streamed, never read whole into memory - the music
                    // library lives in here.
                    using var target = fileEntry.Open();
                    source.CopyTo(target, 81920);
                }
            }
            else if (item is DirectoryInfo folder && !folder.EnumerateFileSystemInfos().Any())
            {
                archive.CreateEntry(entryName + "/");
            }
        }

        if (sealedSecrets is not null)
        {
            var entry = archive.CreateEntry(SecretsFileName, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(sealedSecrets);
        }
    }

    /// <summary>
    /// Every settings file (.json) in the data folder, read into memory
    /// while no save is under way and none can start.
    ///
    /// A backup made while the app is busy - which, now that the automatic
    /// one runs in the background, can be the middle of a race - would
    /// otherwise zip events.json before a change and checkin.json after
    /// it, and restore to a state the app was never in. The settings files
    /// come to a few MB at most, so holding saves off while they are read
    /// is a pause nobody sees; the big files (music, photos) are nothing a
    /// save writes, so they are streamed afterwards without holding
    /// anything. Keyed by zip entry name.
    /// </summary>
    private static Dictionary<string, (byte[] Bytes, DateTime LastWrite)> ReadSettingsFilesTogether(DirectoryInfo root, string basePath)
    {
        var copies = new Dictionary<string, (byte[] Bytes, DateTime LastWrite)>(StringComparer.OrdinalIgnoreCase);

        SafeFile.WhileNoWritesUnderWay(() =>
        {
            // Listed by pattern, so the music library isn't walked file by
            // file while every save waits; the check below is the real test.
            foreach (var file in root.EnumerateFiles("*.json", SearchOption.AllDirectories))
            {
                if (!file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                    || IsLeftOutOfBackup(EntryNameFor(file.FullName, basePath)))
                {
                    continue;
                }

                try
                {
                    using var source = OpenShared(file.FullName);
                    using var buffer = new MemoryStream();
                    source.CopyTo(buffer, 81920);

                    copies[EntryNameFor(file.FullName, basePath)] = (buffer.ToArray(), File.GetLastWriteTime(file.FullName));
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    // Gone since it was listed - left out, as below.
                }
            }
        });

        return copies;
    }

    /// <summary>
    /// Opens a file to copy into a backup.
    ///
    /// Not File.OpenRead / CreateEntryFromFile: those open the file
    /// FileShare.Read, which fails on any file another handle has open for
    /// writing - the iLap raw capture log is held open like that for as
    /// long as the decoder is connected, so every backup made on a race day
    /// failed. Shared read/write/delete here.
    /// </summary>
    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);

    /// <summary>A file's name inside the zip: relative to the data folder, with forward slashes as CreateFromDirectory writes them.</summary>
    private static string EntryNameFor(string fullName, string basePath) =>
        Path.GetFullPath(fullName)[basePath.Length..].Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// A save's own half-written temp file ("events.json.&lt;guid&gt;.tmp",
    /// see SafeFile). It is either about to replace the real file or about
    /// to be deleted; either way it is not data, and restoring one would
    /// only leave clutter in the data folder.
    /// </summary>
    private static bool IsSaveInProgressFile(string fileName) =>
        fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>The app's settings file, the one RestoredSettings compares.</summary>
    private const string SettingsFileName = "settings.json";

    /// <summary>
    /// RestoredSettings' record. Never taken from a backup: it is the
    /// laptop's own note of what a restore brought in, and a backup that
    /// carried one - made on a laptop with something still unconfirmed, or
    /// written to clear the holds in advance - must not decide it.
    /// </summary>
    private static bool IsRestoreRecord(string destination) =>
        string.Equals(
            destination,
            Path.GetFullPath(Path.Combine(DataDirectory, RestoredSettings.FileName)),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>A file's text, or null when it is not there or cannot be read - which RestoredSettings takes as "nothing in use".</summary>
    private static string? ReadIfThere(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a zip entry name is in the undo holding folder, where undoable
    /// actions park the files they would have deleted (see UndoHoldingFolder).
    /// Those are kept a week for this session's Undo and nothing else: in a
    /// backup they would carry deleted music and photos around for good, and
    /// a restore restarts the app, which empties the undo list they were
    /// kept for. So they are left out of every backup, and a restore - of a
    /// backup made before they were left out - neither brings them back nor
    /// touches the ones held now.
    /// </summary>
    private static bool IsUndoHolding(string entryName) =>
        entryName.Equals(UndoHoldingFolder.FolderName, StringComparison.OrdinalIgnoreCase)
        || entryName.StartsWith(UndoHoldingFolder.FolderName + "/", StringComparison.OrdinalIgnoreCase)
        // Driver photos replaced or taken down, held the same way for
        // Undo (see DriverPhotos.Retire) - same reasons.
        || entryName.StartsWith(RetiredDriverPhotosEntry, StringComparison.OrdinalIgnoreCase);

    private const string RetiredDriverPhotosEntry = "DriverPhotos/Retired/";

    /// <summary>
    /// Whether a zip entry name is left out of every backup: the undo
    /// holding folder (see IsUndoHolding), and the photo-finish frames
    /// (PhotoFinish.Root).
    ///
    /// The frames used to go into every backup. Each car taking the flag
    /// keeps a couple of MB of JPEGs, which barely squash, and nothing ever
    /// deletes them, so every automatic backup - daily, fourteen kept, and
    /// copied to the cloud folders too - zipped every night's pictures
    /// again, until the backups filled the laptop's disk. Every shot.json
    /// was also read while every save waited. They are pictures to look at
    /// on the night, not club data. A restore is a separate decision and
    /// still goes by IsUndoHolding alone, so a backup made before this
    /// puts its frames back; frames already on the laptop are never touched.
    /// </summary>
    private static bool IsLeftOutOfBackup(string entryName) =>
        IsUndoHolding(entryName)
        || entryName.Equals(PhotoFinish.FolderName, StringComparison.OrdinalIgnoreCase)
        || entryName.StartsWith(PhotoFinish.FolderName + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>As CreateEntryFromFile does: a zip can't hold a time outside 1980-2107, and the setter throws on one.</summary>
    private static DateTime ZipSafeTime(DateTime lastWrite) =>
        lastWrite.Year < 1980 || lastWrite.Year > 2107
            ? new DateTime(1980, 1, 1, 0, 0, 0)
            : lastWrite;

    /// <summary>
    /// The secrets for a backup, already locked with the backup password,
    /// or null when there is no password (or nothing to ask). The plain
    /// JSON only ever exists in memory here.
    /// </summary>
    private static string? SealSecretsForBackup(BackupSecrets? secrets)
    {
        if (secrets is null || string.IsNullOrEmpty(secrets.Password))
        {
            return null;
        }

        return SecretBox.PasswordSeal(JsonSerializer.Serialize(secrets), secrets.Password);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A leftover .tmp is harmless: a later backup clears it away
            // (see DeleteLeftovers).
        }
    }

    /// <summary>How long a half-written backup must have been left alone before a later backup takes it for one that was cut off.</summary>
    private static readonly TimeSpan LeftoverAge = TimeSpan.FromHours(1);

    /// <summary>
    /// Deletes half-written backups an earlier one left in
    /// <paramref name="folder"/>: this app's backup names ending ".zip"
    /// plus <paramref name="suffix"/> (".tmp" here, ".partial" in a cloud
    /// folder - see CloudBackup.CopyToTargets), untouched for an hour or
    /// more. Never throws.
    ///
    /// A backup cut off partway - HeatCaller closed, Windows shut down, or a
    /// new look picked while the automatic one was still zipping - left its
    /// temp file behind for good. Each one is named for its own backup's
    /// time, so the check for this backup's own name before it starts never
    /// matched one, and the backup list only shows .zip files, so they piled
    /// up unseen: possibly GBs each, and uploaded by the cloud app. The hour
    /// is so a backup still being written somewhere is never taken for one.
    /// </summary>
    internal static void DeleteLeftovers(string folder, string suffix)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "HeatCaller-*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);

                if (!name.EndsWith(".zip" + suffix, StringComparison.OrdinalIgnoreCase)
                    || !(name.StartsWith(ManualPrefix, StringComparison.OrdinalIgnoreCase)
                         || name.StartsWith(AutomaticPrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                try
                {
                    // Asked of the file itself, not the listing, whose
                    // times can lag behind a file still being written.
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= LeftoverAge)
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // In use or gone - tried again by the next backup.
                }
            }
        }
        catch
        {
            // A folder that can't be listed has nothing this can tidy.
        }
    }

    /// <summary>
    /// Restores a backup zip, overwriting current app data. Automatically
    /// takes a safety backup of whatever's CURRENTLY there first (into a
    /// separate "Pre-Restore Safety Backups" subfolder), so restoring the
    /// wrong file - or changing your mind - can itself be undone.
    ///
    /// Throws, with nothing in the data folder touched, if the backup is
    /// refused or can't be unpacked. Once it gets as far as putting files
    /// back it no longer throws: it puts back everything it can and says in
    /// the result what it couldn't.
    ///
    /// <paramref name="beforeReplacing"/> runs once the backup has unpacked
    /// cleanly and just before the first file in the data folder is
    /// replaced - the point of no return. The caller uses it to stop the
    /// app saving (SafeFile.Freeze) and to close files the app holds open
    /// (music, ambience, the decoder's capture log), which Windows won't
    /// let be replaced while they are open. From then on the app must
    /// restart, whatever the result says.
    /// </summary>
    public static RestoreResult RestoreBackup(string zipPath, string? backupFolderForSafetyNet = null, Action? beforeReplacing = null)
    {
        var safetyFolder = string.IsNullOrWhiteSpace(backupFolderForSafetyNet) ? DefaultBackupFolder : backupFolderForSafetyNet;

        using var archive = ZipFile.OpenRead(zipPath);

        // Checked in full BEFORE anything is written - before the safety
        // backup, even - so a bad backup is refused whole rather than
        // half-restored over good data.
        RequireAppBackup(archive);
        var allowance = RestoreAllowance(zipPath);
        var destinations = SafeDestinations(archive, allowance);

        var safetyBackupPath = string.Empty;
        if (Directory.Exists(DataDirectory) && Directory.EnumerateFileSystemEntries(DataDirectory).Any())
        {
            safetyBackupPath = CreateBackup(PreRestoreSafetyFolder(safetyFolder));
        }

        Directory.CreateDirectory(DataDirectory);

        // Unpacked into a folder of its own first, then moved across.
        // Extracting straight over the live folder meant a backup that
        // turned out to be damaged partway through - a corrupt entry, a
        // full disk - left half the data restored and half not, which is
        // worse than either. Now a failure while unpacking leaves the live
        // folder exactly as it was.
        var staging = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(DataDirectory).TrimEnd(Path.DirectorySeparatorChar))!,
            $"HeatCaller.restoring-{Guid.NewGuid():N}");

        var result = new RestoreResult { SafetyBackupPath = safetyBackupPath };

        try
        {
            var root = Path.GetFullPath(DataDirectory);
            var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long remaining = allowance;

            foreach (var (entry, destination) in destinations)
            {
                // A save's half-written temp file, caught by a backup made
                // before these were left out. Not data.
                // The holding-folder test goes by where the entry would
                // actually land, not the name as written: a crafted
                // "./undo-holding/x" or "a/../undo-holding/x" names the same
                // place and must be left out just the same.
                if (IsSaveInProgressFile(entry.Name)
                    || IsUndoHolding(Path.GetRelativePath(root, destination).Replace('\\', '/'))
                    || IsRestoreRecord(destination))
                {
                    continue;
                }

                var stagedPath = Path.GetFullPath(Path.Combine(staging, Path.GetRelativePath(root, destination)));
                if (!PlainFileName.IsInside(staging, stagedPath))
                {
                    throw new InvalidDataException(
                        $"That backup has a file the app can't place (\"{entry.FullName}\"). Nothing has been restored.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                remaining -= ExtractLimited(entry, stagedPath, remaining);

                // Keyed by the staged path, so a zip naming the same file
                // twice is moved across once, not failed on the second go.
                staged[stagedPath] = destination;
            }

            // Which of the backup's addresses - relay, voice relay, mail
            // server - this laptop was not already using, recorded so
            // nothing is sent to them until somebody has looked (see
            // RestoredSettings). Before the point of no return, and allowed
            // to throw: a record that cannot be written refuses the restore
            // whole, rather than putting the addresses in place unguarded.
            var settingsPath = Path.GetFullPath(Path.Combine(DataDirectory, SettingsFileName));
            var stagedSettings = staged.FirstOrDefault(pair =>
                string.Equals(pair.Value, settingsPath, StringComparison.OrdinalIgnoreCase)).Key;

            if (stagedSettings is not null)
            {
                RestoredSettings.Hold(RestoredSettings.WhatNeedsConfirming(
                    ReadIfThere(settingsPath),
                    File.ReadAllText(stagedSettings)));
            }

            // The point of no return. Everything above only read the live
            // folder; everything below changes it.
            try
            {
                beforeReplacing?.Invoke();
            }
            catch
            {
                // The caller's clean-up is a help, not a precondition: a
                // file it failed to close is retried and reported below.
            }

            // A secrets file left from an earlier restore goes, so the one
            // in the data folder is always this backup's - or none at all
            // when this backup carries none. Its own, if it has one, is
            // moved in below (and would replace the old one regardless).
            // Best effort: failing the whole restore over it would help
            // nobody.
            TryDelete(Path.Combine(DataDirectory, SecretsFileName));

            // A backup from before the rename to HeatCaller has paths and
            // voice settings under the old names. Clearing the marker has
            // the restart that follows every restore bring them up to date
            // (see AppDataMigration); for a newer backup it finds nothing
            // to change.
            TryDelete(Path.Combine(DataDirectory, AppDataMigration.MarkerFileName));

            // The same for the race journal (RaceJournal's file). A restore
            // only ever adds and replaces files, so a journal left by a race
            // on THIS machine survived the restore of a backup that had
            // none, and on the restart the app offered to resume a race
            // that belongs to data that is no longer there. Kept only when
            // the backup brings its own, which is moved in below.
            var journalPath = Path.GetFullPath(Path.Combine(DataDirectory, RaceJournalFileName));
            if (!staged.Values.Any(destination => string.Equals(destination, journalPath, StringComparison.OrdinalIgnoreCase)))
            {
                TryDelete(journalPath);
            }

            // The same again for every other store file the backup has no
            // copy of. A restore that only adds and replaces left this
            // laptop's garage, seasons, check-in, relay results and so on
            // beside the backup's drivers and races - a mix of two clubs'
            // data that neither was ever in. Moved into the undo holding
            // folder rather than deleted: they are in the pre-restore
            // safety backup too, so restoring that puts them back, and the
            // holding copy is a second way back for a week. Only the
            // settings files at the top of the data folder - the stores -
            // are touched: music, photos, logs and the holding folder are
            // not, and settings.json and RestoredSettings' record (written
            // just above for this restore) are always left in place.
            SetAsideFilesNotInBackup(root, staged.Values, journalPath, settingsPath, result);

            // The data first, then the music and logs, so that if anything
            // is going to go wrong it is more likely to be with a song than
            // with the driver list. Every file is tried whatever happened to
            // the one before: stopping at the first failure - as this used
            // to - left the folder half old and half new, and trying the
            // rest can only put more of the backup back.
            foreach (var (stagedPath, destination) in staged.OrderBy(pair => IsReplaceable(pair.Value) ? 1 : 0))
            {
                var relative = Path.GetRelativePath(root, destination);

                try
                {
                    MoveIntoPlace(stagedPath, destination);
                }
                catch (Exception ex)
                {
                    if (IsReplaceable(destination))
                    {
                        result.Skipped.Add(relative);
                    }
                    else
                    {
                        result.Failed.Add($"{relative} ({ex.Message})");
                    }
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch
            {
                // A leftover staging folder is harmless, and outside the
                // data folder so it never ends up in a backup.
            }
        }

        return result;
    }

    /// <summary>
    /// Moves each store file (a .json at the top of the data folder) that
    /// the backup being restored has no copy of into a fresh undo holding
    /// folder, so the restored data is the backup's alone rather than the
    /// backup's laid over whatever this laptop had that it didn't. Never
    /// throws: a file that can't be moved is left as it was and named in
    /// the result's Failed list, which the Backups page already reads as
    /// "still holds the data from before the restore".
    /// </summary>
    private static void SetAsideFilesNotInBackup(string root, IEnumerable<string> restoredDestinations, string journalPath, string settingsPath, RestoreResult result)
    {
        List<string> live;
        try
        {
            live = Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly).ToList();
        }
        catch
        {
            return;
        }

        var restored = new HashSet<string>(restoredDestinations, StringComparer.OrdinalIgnoreCase);
        string? holding = null;

        foreach (var file in live)
        {
            var fullPath = Path.GetFullPath(file);

            if (!Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase)
                || restored.Contains(fullPath)
                || IsRestoreRecord(fullPath)
                || string.Equals(fullPath, settingsPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, journalPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            holding ??= UndoHoldingFolder.NewFolder();

            if (UndoHoldingFolder.MoveIn(fullPath, holding) is null && File.Exists(fullPath))
            {
                result.Failed.Add($"{Path.GetFileName(fullPath)} (not in the backup, and couldn't be set aside)");
            }
        }
    }

    /// <summary>
    /// Moves one unpacked file over its live copy, trying again for a
    /// moment if the live copy is in use.
    ///
    /// Windows refuses to replace a file another handle has open without
    /// delete-sharing - a track the music player still has open, an
    /// antivirus scan, the decoder's capture log - and most of those let
    /// go within a second. Anything still held after that is reported by
    /// the caller rather than stopping the restore.
    /// </summary>
    private static void MoveIntoPlace(string stagedPath, string destination)
    {
        const int attempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(stagedPath, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < attempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>
    /// A file the app works perfectly well without the backup's copy of:
    /// the music library, other audio, and logs. If one of these is locked,
    /// the file already in its place is kept, and the restore carries on
    /// and says so rather than calling itself a failure over a song.
    /// </summary>
    private static bool IsReplaceable(string destination)
    {
        if (PlainFileName.IsInside(MusicStore.MusicDirectory, destination))
        {
            return true;
        }

        var extension = Path.GetExtension(destination);
        return extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Refuses a zip that isn't one of this app's backups, before anything
    /// is touched. Any zip could be picked from the Restore dialog, and
    /// unpacking an unrelated one would scatter its files into the data
    /// folder and then restart as if all was well. Every real backup has
    /// the settings or driver profiles at its top level.
    /// </summary>
    private static void RequireAppBackup(ZipArchive archive)
    {
        var looksLikeOurs = archive.Entries.Any(e =>
            e.FullName.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
            || e.FullName.Equals("profiles.json", StringComparison.OrdinalIgnoreCase));

        if (!looksLikeOurs)
        {
            throw new InvalidDataException(
                "That zip isn't a backup made by this app - it has no settings or driver profiles in it. Nothing has been restored.");
        }
    }

    /// <summary>
    /// Unpacks one entry, stopping as soon as it passes what's left of the
    /// size allowance. The size a zip declares for an entry is just a
    /// number in the file and can be anything; this counts what actually
    /// comes out. Returns the bytes written.
    /// </summary>
    private static long ExtractLimited(ZipArchiveEntry entry, string path, long budget)
    {
        using var input = entry.Open();
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long written = 0;
        int read;

        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            if (written > budget)
            {
                throw new InvalidDataException("That backup unpacks to far more than any real backup could. Nothing has been restored.");
            }

            output.Write(buffer, 0, read);
        }

        return written;
    }

    /// <summary>The most a restore will unpack. Years of race nights come to a few hundred MB at most.</summary>
    private const long MaxRestoreBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// How many times its own size a backup over MaxRestoreBytes may unpack
    /// to. The club's data is mostly text, which squashes perhaps ten to one;
    /// music, audio and photos barely squash at all. A crafted zip that
    /// fills the disk inflates by hundreds or thousands to one.
    /// </summary>
    private const long MaxRestoreRatio = 20;

    /// <summary>
    /// The most this particular backup may unpack to: MaxRestoreBytes, or
    /// more for a backup that is itself big.
    ///
    /// The allowance used to be MaxRestoreBytes flat, while backups are made
    /// with no limit at all - and the music library lives in the data
    /// folder, with nothing capping it. A club with a couple of GB of
    /// race-night playlist got a "successful" backup every night that every
    /// restore then refused as too big, the Pre-Restore Safety Backups
    /// included. What tells a crafted zip from a real one is how far it
    /// inflates, not how much it holds, so a big backup is allowed what its
    /// own size on disk accounts for. The size is the zip file's real one,
    /// not anything the zip says about itself.
    /// </summary>
    private static long RestoreAllowance(string zipPath)
    {
        var zipBytes = new FileInfo(zipPath).Length;
        return Math.Max(MaxRestoreBytes, zipBytes > long.MaxValue / MaxRestoreRatio ? long.MaxValue : zipBytes * MaxRestoreRatio);
    }

    /// <summary>
    /// Where each entry in a backup would land - or an exception, before
    /// anything is written, if any of them would land anywhere else.
    ///
    /// A zip names its files, and nothing stops a zip from naming
    /// "..\..\Startup\run.bat" or "C:\Windows\x". Combined into a path and
    /// extracted, those write wherever they point ("zip slip"), so a
    /// backup somebody sent you could have put a file anywhere this
    /// Windows account can write. Every entry must resolve to somewhere
    /// inside the app's own data folder, and the whole backup must unpack
    /// to a sane size (<paramref name="allowance"/>, see RestoreAllowance) -
    /// a small zip that inflates to fill the disk is the other way a crafted
    /// file hurts.
    /// </summary>
    private static Dictionary<ZipArchiveEntry, string> SafeDestinations(ZipArchive archive, long allowance)
    {
        var root = Path.GetFullPath(DataDirectory);

        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        var destinations = new Dictionary<ZipArchiveEntry, string>();
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));

            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"That backup tries to write outside the app's data folder (\"{entry.FullName}\"). It was not made by this app, and nothing has been restored.");
            }

            total += entry.Length;

            if (total > allowance)
            {
                throw new InvalidDataException("That backup unpacks to far more than any real backup could. Nothing has been restored.");
            }

            destinations[entry] = destination;
        }

        return destinations;
    }

    /// <summary>Lists this app's backups in a folder - manual and automatic - newest first. Excludes the Pre-Restore Safety Backups subfolder from this listing - those are recovery-only, not meant to clutter the normal backup list. Any other zip in the folder is not ours and isn't listed; it can still be restored by picking it with Restore from File.</summary>
    public static IReadOnlyList<FileInfo> ListBackups(string? folder = null)
    {
        var dir = string.IsNullOrWhiteSpace(folder) ? DefaultBackupFolder : folder;
        if (!Directory.Exists(dir))
        {
            return Array.Empty<FileInfo>();
        }

        return new DirectoryInfo(dir)
            .GetFiles("*.zip", SearchOption.TopDirectoryOnly)
            .Where(f => BackupPrefix(f) is not null)
            .OrderByDescending(BackupTime)
            .ToList();
    }

    /// <summary>Which of this app's prefixes a file's name starts with, or null if it isn't one of its backups.</summary>
    private static string? BackupPrefix(FileInfo file)
    {
        if (!file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (file.Name.StartsWith(AutomaticPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AutomaticPrefix;
        }

        return file.Name.StartsWith(ManualPrefix, StringComparison.OrdinalIgnoreCase) ? ManualPrefix : null;
    }

    /// <summary>
    /// When a backup was taken, from the time in its name. The file's own
    /// modified time changes when it's copied between drives or machines,
    /// which is exactly what people do with backups - so ordering by it
    /// could make pruning delete the newest. Falls back to it only for a
    /// name without a readable time. Also what the Backups page shows, so
    /// the date beside each backup is the one it is sorted by.
    /// </summary>
    internal static DateTime BackupTime(FileInfo file)
    {
        var prefix = BackupPrefix(file);
        if (prefix is not null)
        {
            var stamp = Path.GetFileNameWithoutExtension(file.Name)[prefix.Length..];
            if (DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken))
            {
                return taken;
            }
        }

        return file.LastWriteTime;
    }

    /// <summary>
    /// Deletes the oldest AUTOMATIC backups beyond the configured retention
    /// count, so they don't accumulate forever. Backups made with Backup
    /// Now, and anything else in the folder, are never touched.
    /// </summary>
    public static void PruneOldBackups(string folder, int keepCount)
    {
        var automatic = ListBackups(folder)
            .Where(f => BackupPrefix(f) == AutomaticPrefix)
            .ToList();

        foreach (var old in automatic.Skip(Math.Max(1, keepCount)))
        {
            try
            {
                old.Delete();
            }
            catch
            {
                // Best effort - a locked/in-use file just survives until next time.
            }
        }
    }
}

/// <summary>
/// What a backup carries of the secrets, locked with the backup password -
/// so a restore on a new laptop can get the keys back, including the badge
/// key every printed badge depends on. Serialized only to be locked with
/// SecretBox.PasswordSeal; never written anywhere as it is.
/// </summary>
internal sealed class BackupSecrets
{
    /// <summary>The app's own secrets, as AppSettings.CaptureSecrets hands them over.</summary>
    public AppSettings.SecretFields? App { get; set; }

    /// <summary>The ElevenLabs key, which lives in the voice settings rather than the app's.</summary>
    public string? ElevenLabsKey { get; set; }

    /// <summary>
    /// The password to lock this with. Not part of what gets locked -
    /// whoever can open it has typed it already.
    /// </summary>
    [JsonIgnore]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The secrets as they stand, or null when there is no backup password
    /// (no password, no secrets in backups). What MainForm hands to
    /// BackupService.SecretsProvider.
    /// </summary>
    public static BackupSecrets? Capture(AppSettings settings, VoiceSettingsStore voice)
    {
        if (string.IsNullOrEmpty(settings.BackupPassword))
        {
            return null;
        }

        return new BackupSecrets
        {
            App = settings.CaptureSecrets(),
            ElevenLabsKey = voice.Current.ApiKey,
            Password = settings.BackupPassword,
        };
    }
}

/// <summary>
/// What a restore that got as far as putting files back actually did.
/// Once files start going back the app has to restart whatever this says -
/// but a restore that left the driver list behind needs saying very
/// differently from one that left a song behind.
/// </summary>
public sealed class RestoreResult
{
    /// <summary>Where the data from before the restore was saved, or empty when there was nothing to save.</summary>
    public string SafetyBackupPath { get; init; } = string.Empty;

    /// <summary>Music, audio and log files that were in use and left as they were. The app runs fine without them.</summary>
    public List<string> Skipped { get; } = new();

    /// <summary>Data files that could not be put back, each with the reason. Empty when the restore is complete.</summary>
    public List<string> Failed { get; } = new();

    public bool IsComplete => Failed.Count == 0;
}
