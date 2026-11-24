namespace HeatCaller.Tests;

/// <summary>
/// Backups (23 Nov 2026, bug hunt 24):
///
/// Only a locked backup may go to the cloud (privacy plan 2.4). That was
/// kept for the ticked cloud copies, and when a folder was picked on the
/// Backups page - but not when a backup was made. A backup folder already
/// inside OneDrive, Google Drive or iCloud Drive (picked before the check,
/// the password taken off afterwards, or the default folder on a PC where
/// OneDrive looks after Documents) had every backup written there as a
/// plain zip. A backup with no password now stays on this PC instead.
///
/// The sync folders here are made up and never exist on disk, so nothing
/// depends on what is installed on the PC running the tests.
/// </summary>
public sealed class BugHunt24_BackupServiceTests : IDisposable
{
    private readonly TempAppData _data = new();

    public BugHunt24_BackupServiceTests()
    {
        BackupService.SyncRootOverride = Roots;

        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), "[{\"DriverName\":\"Mia Kent\"}]");
        File.WriteAllText(_data.PathOf("settings.json"), "{}");
    }

    public void Dispose()
    {
        BackupService.SyncRootOverride = null;
        _data.Dispose();
    }

    private string OneDrive => Path.Combine(_data.Folder, "OneDrive");

    private string? Roots(CloudService service) => service == CloudService.OneDrive ? OneDrive : null;

    private string CloudFolder => Path.Combine(OneDrive, "Documents", "HeatCaller Backups");

    private static bool IsInside(string root, string path) => PlainFileName.IsInside(root, path);

    [Fact]
    public void A_backup_with_no_password_is_not_written_inside_a_cloud_folder()
    {
        var path = BackupService.CreateBackup(CloudFolder, automatic: false, secrets: null);

        Assert.True(File.Exists(path));
        Assert.False(BackupService.IsLocked(path));
        Assert.False(IsInside(OneDrive, path), $"an unlocked backup went to the cloud folder: {path}");
        Assert.True(IsInside(BackupService.LocalOnlyBackupFolder, path));
        Assert.False(Directory.Exists(OneDrive), "nothing at all should have been made in the cloud folder");
    }

    [Fact]
    public void A_locked_backup_still_goes_to_the_chosen_cloud_folder()
    {
        var path = BackupService.CreateBackup(CloudFolder, automatic: false, new BackupSecrets { Password = "club pass" });

        Assert.True(BackupService.IsLocked(path));
        Assert.True(IsInside(CloudFolder, path));
    }

    [Fact]
    public void A_backup_with_no_password_outside_any_cloud_folder_goes_where_it_was_sent()
    {
        var local = Path.Combine(_data.Folder, "backups");

        var path = BackupService.CreateBackup(local, automatic: false, secrets: null);

        Assert.True(IsInside(local, path));
    }

    [Fact]
    public void The_rule_counts_a_default_folder_under_OneDrive_Documents_as_the_cloud()
    {
        var localOnly = Path.Combine(_data.Folder, "this pc only");
        var elsewhere = Path.Combine(_data.Folder, "Documents", "HeatCaller Backups");

        // Not locked, inside the sync folder (or the sync folder itself): kept on this PC.
        Assert.Equal(localOnly, BackupService.FolderForBackup(CloudFolder, locked: false, localOnly, Roots));
        Assert.Equal(localOnly, BackupService.FolderForBackup(OneDrive, locked: false, localOnly, Roots));

        // Locked, or nowhere near a sync folder: exactly where it was sent.
        Assert.Equal(CloudFolder, BackupService.FolderForBackup(CloudFolder, locked: true, localOnly, Roots));
        Assert.Equal(elsewhere, BackupService.FolderForBackup(elsewhere, locked: false, localOnly, Roots));
        Assert.Equal(elsewhere, BackupService.FolderForBackup(elsewhere, locked: false, localOnly, _ => null));
    }

    [Fact]
    public void A_safety_backup_kept_off_the_cloud_stays_in_its_own_subfolder()
    {
        var localOnly = Path.Combine(_data.Folder, "this pc only");
        var safety = Path.Combine(CloudFolder, "Pre-Restore Safety Backups");

        Assert.Equal(
            Path.Combine(localOnly, "Pre-Restore Safety Backups"),
            BackupService.FolderForBackup(safety, locked: false, localOnly, Roots));

        // And through a real restore: the safety backup of what was there
        // before must not be the plain zip that reaches the cloud either.
        var zip = BackupService.CreateBackup(Path.Combine(_data.Folder, "backups"), automatic: false, secrets: null);

        var result = BackupService.RestoreBackup(zip, CloudFolder);

        Assert.True(File.Exists(result.SafetyBackupPath));
        Assert.True(IsInside(
            Path.Combine(BackupService.LocalOnlyBackupFolder, "Pre-Restore Safety Backups"),
            result.SafetyBackupPath));
        Assert.False(Directory.Exists(OneDrive));

        // Out of the ordinary list, as safety backups always are.
        Assert.Empty(BackupService.ListBackups(BackupService.LocalOnlyBackupFolder));
    }

    [Fact]
    public void Automatic_backups_kept_off_the_cloud_do_not_pile_up()
    {
        // The caller prunes the folder it asked for - the cloud one - so
        // the folder these really go to has to be kept tidy here.
        var local = BackupService.LocalOnlyBackupFolder;
        Directory.CreateDirectory(local);

        for (var day = 1; day <= 20; day++)
        {
            File.WriteAllText(Path.Combine(local, $"HeatCaller-AutoBackup-2026-10-{day:00}_20-00-00.zip"), "old");
        }

        var made = Path.Combine(local, "HeatCaller-Backup-2026-10-01_09-00-00.zip");
        File.WriteAllText(made, "made on purpose");

        var path = BackupService.CreateBackup(CloudFolder, automatic: true, secrets: null);

        var kept = BackupService.ListBackups(local);
        Assert.Equal(14, kept.Count(f => f.Name.StartsWith("HeatCaller-AutoBackup-", StringComparison.Ordinal)));
        Assert.Contains(kept, f => string.Equals(f.FullName, path, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(made), "a backup made on purpose is never pruned");
    }

    [Fact]
    public void The_folder_for_backups_kept_on_this_PC_is_beside_the_data_not_in_it()
    {
        var local = BackupService.LocalOnlyBackupFolder;

        Assert.False(IsInside(AppPaths.Root, local));
        Assert.False(string.Equals(Path.GetFullPath(local), Path.GetFullPath(AppPaths.Root), StringComparison.OrdinalIgnoreCase));

        // And sent there, it stays there: the rule does not move it again.
        Assert.Equal(local, BackupService.FolderForBackup(local, locked: false, local, Roots));
    }
}
