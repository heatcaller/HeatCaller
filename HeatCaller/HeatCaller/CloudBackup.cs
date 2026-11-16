namespace HeatCaller;

/// <summary>A cloud storage service a copy of each backup can go to.</summary>
internal enum CloudService
{
    GoogleDrive,
    ICloudDrive,
    OneDrive,
}

/// <summary>
/// Copies of every backup in the club's cloud storage - Google Drive, iCloud
/// Drive or OneDrive - so a lost or broken laptop doesn't take the backups
/// with it.
///
/// It works through each service's own Windows app (Google Drive for
/// desktop, iCloud for Windows, OneDrive), not through an online API: the
/// app is already signed in and already syncs a folder on this PC, so a
/// backup copied into that folder is uploaded by it. That means no account
/// to connect here, no passwords or tokens for this app to hold, and it
/// keeps working offline - the copy is uploaded when the laptop is next
/// online. The cost is that the service's app has to be installed and
/// signed in, which the Backup &amp; Restore page checks and says.
///
/// Copies go into a "HeatCaller Backups" folder at the top of that storage.
/// Automatic backups there are pruned to the same count as the local
/// folder; manual ones are never deleted, the same rule as locally. The
/// rehearsal copy (see Rehearsal) never copies anything there.
/// </summary>
internal static class CloudBackup
{
    public const string FolderName = "HeatCaller Backups";

    /// <summary>A service that is switched on, and where its copies go (null when its sync folder isn't on this PC).</summary>
    internal sealed record Target(CloudService Service, string Name, string? Folder);

    /// <summary>
    /// What one target's copy did: whether it got there, and the line said
    /// about it. The line alone used to be all a caller got, so telling a
    /// copy that worked from one that didn't meant reading its wording -
    /// see CloudBackupStatus, which records the answer for the light.
    /// </summary>
    internal sealed record CopyOutcome(CloudService Service, bool Copied, string Line);

    /// <summary>Why a backup is kept out of the cloud (15 Nov 2026, privacy plan 2.4).</summary>
    public const string NeedsPassword =
        "set a backup password first. Backups hold every driver's details, so only locked ones go to the cloud.";

    /// <summary>
    /// What to say when a cloud copy is switched on, or a backup folder
    /// inside a cloud folder picked, with no backup password - or null
    /// when it may go ahead. One rule for every switch on the Backups page.
    /// </summary>
    public static string? RefusalWithoutPassword(AppSettings settings, string where) =>
        string.IsNullOrEmpty(settings.BackupPassword)
            ? $"Set a backup password first (on this page), then {where}.\n\n"
              + "Backups hold every driver's details - children's too - so they only go to the cloud locked with the club's password."
            : null;

    /// <summary>
    /// The cloud service whose sync folder holds <paramref name="folder"/>,
    /// or null when none does. Never throws.
    /// </summary>
    public static string? ServiceHolding(string folder, Func<CloudService, string?>? syncRoot = null)
    {
        foreach (var service in Enum.GetValues<CloudService>())
        {
            try
            {
                var root = (syncRoot ?? FindSyncRoot)(service);
                if (root is not null
                    && (PlainFileName.IsInside(root, folder)
                        || string.Equals(Path.GetFullPath(root).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                {
                    return NameOf(service);
                }
            }
            catch
            {
                // A folder that can't be compared isn't that service's.
            }
        }

        return null;
    }

    public static string NameOf(CloudService service) => service switch
    {
        CloudService.GoogleDrive => "Google Drive",
        CloudService.ICloudDrive => "iCloud Drive",
        _ => "OneDrive",
    };

    /// <summary>What to install when a service's sync folder can't be found.</summary>
    public static string AppNameOf(CloudService service) => service switch
    {
        CloudService.GoogleDrive => "Google Drive for desktop",
        CloudService.ICloudDrive => "iCloud for Windows",
        _ => "OneDrive",
    };

    public static bool IsEnabled(AppSettings settings, CloudService service) => service switch
    {
        CloudService.GoogleDrive => settings.CloudBackupGoogleDrive,
        CloudService.ICloudDrive => settings.CloudBackupICloudDrive,
        _ => settings.CloudBackupOneDrive,
    };

    public static void SetEnabled(AppSettings settings, CloudService service, bool enabled)
    {
        switch (service)
        {
            case CloudService.GoogleDrive:
                settings.CloudBackupGoogleDrive = enabled;
                break;
            case CloudService.ICloudDrive:
                settings.CloudBackupICloudDrive = enabled;
                break;
            default:
                settings.CloudBackupOneDrive = enabled;
                break;
        }
    }

    /// <summary>
    /// The folder the service syncs on this PC, or null when it isn't
    /// installed and signed in here. Never throws.
    /// </summary>
    public static string? FindSyncRoot(CloudService service)
    {
        try
        {
            return service switch
            {
                CloudService.GoogleDrive => FindGoogleDrive(),
                CloudService.ICloudDrive => FindICloudDrive(),
                _ => FindOneDrive(),
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The services switched on in settings, each with the folder its
    /// copies go to. Read on the UI thread, where settings are safe to read;
    /// the copying happens later, on a background thread.
    /// </summary>
    public static IReadOnlyList<Target> EnabledTargets(AppSettings settings)
    {
        var targets = new List<Target>();

        foreach (var service in Enum.GetValues<CloudService>())
        {
            if (!IsEnabled(settings, service))
            {
                continue;
            }

            var root = FindSyncRoot(service);
            targets.Add(new Target(service, NameOf(service), root is null ? null : Path.Combine(root, FolderName)));
        }

        return targets;
    }

    /// <summary>
    /// Copies a finished backup to every target and prunes old automatic
    /// copies there. Returns one line per target saying what happened -
    /// never throws, because a cloud copy failing must not look like the
    /// backup itself failed.
    /// </summary>
    public static IReadOnlyList<string> CopyToTargets(string backupPath, IReadOnlyList<Target> targets, int keepAutomatic)
        => CopyToTargets(backupPath, targets, keepAutomatic, AppPaths.IsRehearsal);

    /// <summary>
    /// CopyToTargets with whether this run is the rehearsal copy passed in,
    /// so a test can ask for either without switching the whole process
    /// into a rehearsal.
    /// </summary>
    internal static IReadOnlyList<string> CopyToTargets(string backupPath, IReadOnlyList<Target> targets, int keepAutomatic, bool isRehearsal)
        => Copy(backupPath, targets, keepAutomatic, isRehearsal).Select(o => o.Line).ToList();

    /// <summary>CopyToTargets, with whether each target got the copy as well as the line about it - see CopyOutcome.</summary>
    public static IReadOnlyList<CopyOutcome> Copy(string backupPath, IReadOnlyList<Target> targets, int keepAutomatic)
        => Copy(backupPath, targets, keepAutomatic, AppPaths.IsRehearsal);

    /// <summary>Copy with the rehearsal flag passed in, for the same reason CopyToTargets has one.</summary>
    internal static IReadOnlyList<CopyOutcome> Copy(string backupPath, IReadOnlyList<Target> targets, int keepAutomatic, bool isRehearsal)
    {
        var results = new List<CopyOutcome>();

        foreach (var target in targets)
        {
            // A rehearsal copy never touches the club's cloud storage.
            //
            // The rehearsal copies settings.json as it is, so the cloud
            // switches the club turned on are on in the copy too - and the
            // sync folders are found from this Windows user's profile, which
            // is the same for a rehearsal as for the real app. Without this,
            // a rehearsal's practice backup would be copied into the club's
            // real "HeatCaller Backups" folder (and uploaded, where it would
            // look like the club's newest backup), and the pruning below,
            // which keeps only the newest few automatic backups, would delete
            // the club's oldest real cloud backup to make room for it. That
            // is the same harm BackupService.SafeBackupFolder keeps away from
            // the local backup folder. Checked here, at the moment of
            // copying, rather than by switching the settings off when the
            // rehearsal is made, so a rehearsal folder made by an earlier
            // version - with the switches still on - is covered as well.
            if (isRehearsal)
            {
                results.Add(new CopyOutcome(target.Service, false, $"Not copied to {target.Name}: this is the rehearsal copy of HeatCaller, whose backups stay in its own folder."));
                continue;
            }

            if (target.Folder is null)
            {
                results.Add(new CopyOutcome(target.Service, false, $"Not copied to {target.Name}: {AppNameOf(target.Service)} isn't installed or signed in on this PC."));
                continue;
            }

            // 15 Nov 2026 (privacy plan 2.4): only a locked backup goes to
            // the cloud. A backup is every driver's details - children's
            // among them - and a copy on somebody's Google Drive outlives
            // the laptop and whoever looks after it. Checked on the file
            // itself, not the setting, so nothing unlocked slips past
            // whichever way the backup was made.
            if (!BackupCrypto.IsEncrypted(backupPath))
            {
                results.Add(new CopyOutcome(target.Service, false, $"Not copied to {target.Name}: {NeedsPassword}"));
                continue;
            }

            string? partial = null;

            try
            {
                // The local backup folder may itself be inside the cloud
                // folder - there is nothing to copy then.
                var source = Path.GetFullPath(Path.GetDirectoryName(backupPath)!).TrimEnd(Path.DirectorySeparatorChar);
                var destinationFolder = Path.GetFullPath(target.Folder).TrimEnd(Path.DirectorySeparatorChar);

                if (!string.Equals(source, destinationFolder, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(destinationFolder);

                    // Copies an earlier backup left half-done, the same way
                    // as the local folder's .tmp files - and by age alone:
                    // another backup's copy can be under way right now.
                    BackupService.DeleteLeftovers(destinationFolder, ".partial");

                    var destination = Path.Combine(destinationFolder, Path.GetFileName(backupPath));

                    // Written under another name and renamed when complete,
                    // so the sync app never uploads half a backup, and a
                    // half-written file is never listed as one.
                    partial = destination + ".partial";
                    File.Copy(backupPath, partial, overwrite: true);
                    File.Move(partial, destination, overwrite: true);
                    partial = null;
                }

                BackupService.PruneOldBackups(destinationFolder, keepAutomatic);
                results.Add(new CopyOutcome(target.Service, true, $"Copied to {target.Name}."));
            }
            catch (Exception ex)
            {
                // A copy that failed (a full cloud folder, the file held by
                // the sync app) used to leave its half-written copy behind.
                if (partial is not null)
                {
                    try
                    {
                        File.Delete(partial);
                    }
                    catch
                    {
                        // Cleared by a later backup's copy instead.
                    }
                }

                results.Add(new CopyOutcome(target.Service, false, $"Not copied to {target.Name}: {ex.Message}"));
            }
        }

        return results;
    }

    // --- Finding each service's folder -----------------------------------

    /// <summary>
    /// Google Drive for desktop shows the account as its own drive (G: by
    /// default, but any letter), labelled "Google Drive", with the files in
    /// "My Drive". Set to mirror instead, it keeps "My Drive" in the user's
    /// profile, and the older Backup and Sync app used "Google Drive" there.
    /// </summary>
    private static string? FindGoogleDrive()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                // Network drives skipped before anything is ASKED of them.
                //
                // IsReady and VolumeLabel both go out to the server on a
                // mapped drive, and a share that is not there any more -
                // the club laptop at home, off the track's network - blocks
                // each of them until SMB gives up, which is tens of seconds.
                // This runs while the backup settings are being drawn, on
                // the UI thread, so the window simply stopped responding.
                //
                // Deliberately NOT an allow-list of DriveType.Fixed: Google
                // Drive's virtual drive has reported different types across
                // its versions, and a list of what is allowed would quietly
                // stop finding it on one of them. Only the type that hangs
                // is turned away.
                if (drive.DriveType is DriveType.Network or DriveType.NoRootDirectory)
                {
                    continue;
                }

                if (!drive.IsReady)
                {
                    continue;
                }

                var myDrive = Path.Combine(drive.RootDirectory.FullName, "My Drive");

                if (drive.VolumeLabel.Contains("Google Drive", StringComparison.OrdinalIgnoreCase) && Directory.Exists(myDrive))
                {
                    return myDrive;
                }
            }
            catch
            {
                // A drive that can't be asked (an empty card reader, a
                // network drive that has gone) is simply not Google Drive.
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var candidate in new[]
                 {
                     Path.Combine(profile, "My Drive"),
                     Path.Combine(profile, "Google Drive", "My Drive"),
                     Path.Combine(profile, "Google Drive"),
                 })
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// iCloud for Windows - the Microsoft Store version and the older
    /// download alike - keeps iCloud Drive in the user's profile.
    /// </summary>
    private static string? FindICloudDrive()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Path.Combine(profile, "iCloudDrive");

        return Directory.Exists(folder) ? folder : null;
    }

    /// <summary>OneDrive tells Windows where its folder is, personal or work.</summary>
    private static string? FindOneDrive()
    {
        foreach (var variable in new[] { "OneDriveConsumer", "OneDrive", "OneDriveCommercial" })
        {
            var folder = Environment.GetEnvironmentVariable(variable);

            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                return folder;
            }
        }

        return null;
    }
}
