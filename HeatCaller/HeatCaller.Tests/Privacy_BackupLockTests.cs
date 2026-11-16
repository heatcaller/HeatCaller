using System.IO.Compression;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// 15 Nov 2026, children's-privacy plan 2.4: with a backup password the
/// whole backup is locked (BackupCrypto), restore unlocks it, old plain
/// backups still restore, cloud copies need a password, and freeze dumps
/// go after two weeks.
/// </summary>
public sealed class Privacy_BackupLockTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string Backups => Path.Combine(_data.Folder, "backups");

    private static byte[] Lock(byte[] plain, string password)
    {
        using var output = new MemoryStream();
        using (var locked = BackupCrypto.Encrypt(new NonClosing(output), password, iterations: 10_000))
        {
            locked.Write(plain);
        }

        return output.ToArray();
    }

    private static byte[] Unlock(byte[] locked, string password)
    {
        using var output = new MemoryStream();
        BackupCrypto.Decrypt(new MemoryStream(locked), output, password);
        return output.ToArray();
    }

    [Fact]
    public void Locked_bytes_come_back_whole_across_many_pieces()
    {
        var plain = new byte[(int)(2.5 * 1024 * 1024)];
        new Random(7).NextBytes(plain);

        var locked = Lock(plain, "club pass");

        Assert.True(locked.AsSpan(0, BackupCrypto.Magic.Length).SequenceEqual(BackupCrypto.Magic));
        Assert.Equal(plain, Unlock(locked, "club pass"));
    }

    [Fact]
    public void A_wrong_password_is_said_as_that()
    {
        var locked = Lock(Encoding.UTF8.GetBytes("drivers"), "right");

        Assert.Throws<WrongBackupPasswordException>(() => Unlock(locked, "wrong"));
    }

    [Fact]
    public void A_locked_backup_cut_short_or_tampered_with_is_refused()
    {
        var plain = new byte[3 * 1024 * 1024];
        var locked = Lock(plain, "pw");

        // The last piece cut off - a copy that never finished.
        Assert.Throws<InvalidDataException>(() => Unlock(locked[..(locked.Length - 40)], "pw"));

        // A byte changed in the second piece.
        var tampered = (byte[])locked.Clone();
        tampered[(int)(1.5 * 1024 * 1024)] ^= 1;
        Assert.Throws<InvalidDataException>(() => Unlock(tampered, "pw"));
    }

    [Fact]
    public void A_backup_made_with_a_password_is_locked_whole_and_restores_with_it()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), "[{\"Transponder\":\"1234\",\"DriverName\":\"Mia Kent\"}]");
        File.WriteAllText(_data.PathOf("settings.json"), "{}");

        var path = BackupService.CreateBackup(Backups, automatic: false, new BackupSecrets { Password = "club pass" });

        Assert.True(BackupService.IsLocked(path));
        Assert.DoesNotContain("Mia Kent", File.ReadAllText(path, Encoding.Latin1));
        Assert.ThrowsAny<InvalidDataException>(() => ZipFile.OpenRead(path).Dispose());

        File.WriteAllText(_data.PathOf("profiles.json"), "[]");

        Assert.Throws<BackupLockedException>(() => BackupService.RestoreBackup(path, Backups));
        Assert.Throws<WrongBackupPasswordException>(() => BackupService.RestoreBackup(path, Backups, password: "nope"));
        Assert.Equal("[]", File.ReadAllText(_data.PathOf("profiles.json")));

        var result = BackupService.RestoreBackup(path, Backups, password: "club pass");

        Assert.Empty(result.Failed);
        Assert.Contains("Mia Kent", File.ReadAllText(_data.PathOf("profiles.json")));

        // Nothing unlocked is left lying about beside the data folder.
        var parent = Path.GetDirectoryName(Path.GetFullPath(AppPaths.Root).TrimEnd('\\'))!;
        Assert.Empty(Directory.GetFiles(parent, "HeatCaller.unlocking-*"));
    }

    [Fact]
    public void A_backup_with_no_password_is_a_plain_zip_and_still_restores()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), "[{\"DriverName\":\"Sam\"}]");

        var path = BackupService.CreateBackup(Backups, automatic: false, secrets: null);

        Assert.False(BackupService.IsLocked(path));
        using (ZipFile.OpenRead(path))
        {
        }

        File.WriteAllText(_data.PathOf("profiles.json"), "[]");
        BackupService.RestoreBackup(path, Backups);
        Assert.Contains("Sam", File.ReadAllText(_data.PathOf("profiles.json")));
    }

    [Fact]
    public void The_saved_password_is_used_only_when_it_opens_the_backup()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("settings.json"), "{}");
        var path = BackupService.CreateBackup(Backups, automatic: false, new BackupSecrets { Password = "this laptop" });

        Assert.Equal("this laptop", BackupRestoreControl.SavedPasswordThatOpens(path, "this laptop"));
        Assert.Null(BackupRestoreControl.SavedPasswordThatOpens(path, "another club"));
        Assert.Null(BackupRestoreControl.SavedPasswordThatOpens(path, string.Empty));
    }

    [Fact]
    public void Only_a_locked_backup_goes_to_the_cloud()
    {
        var cloud = Path.Combine(_data.Folder, "cloud", CloudBackup.FolderName);
        var local = Path.Combine(_data.Folder, "local");
        Directory.CreateDirectory(local);
        var targets = new[] { new CloudBackup.Target(CloudService.GoogleDrive, "Google Drive", cloud) };

        var plain = Path.Combine(local, "HeatCaller-AutoBackup-2026-11-15_20-00-00.zip");
        File.WriteAllText(plain, "PK plain zip");
        var refused = CloudBackup.Copy(plain, targets, keepAutomatic: 3, isRehearsal: false);

        Assert.False(refused[0].Copied);
        Assert.Contains("backup password", refused[0].Line);
        Assert.False(File.Exists(Path.Combine(cloud, Path.GetFileName(plain))));

        var locked = Path.Combine(local, "HeatCaller-AutoBackup-2026-11-16_20-00-00.zip");
        File.WriteAllBytes(locked, Lock(new byte[10], "pw"));
        var copied = CloudBackup.Copy(locked, targets, keepAutomatic: 3, isRehearsal: false);

        Assert.True(copied[0].Copied);
        Assert.True(File.Exists(Path.Combine(cloud, Path.GetFileName(locked))));
    }

    [Fact]
    public void Switching_a_cloud_copy_on_needs_a_backup_password()
    {
        Assert.NotNull(CloudBackup.RefusalWithoutPassword(new AppSettings(), "tick OneDrive"));
        Assert.Null(CloudBackup.RefusalWithoutPassword(new AppSettings { BackupPassword = "x" }, "tick OneDrive"));
    }

    [Fact]
    public void A_folder_inside_a_sync_folder_is_known_as_that_service()
    {
        var root = Path.Combine(_data.Folder, "OneDrive");
        string? Roots(CloudService s) => s == CloudService.OneDrive ? root : null;

        Assert.Equal("OneDrive", CloudBackup.ServiceHolding(Path.Combine(root, "Backups"), Roots));
        Assert.Equal("OneDrive", CloudBackup.ServiceHolding(root, Roots));
        Assert.Null(CloudBackup.ServiceHolding(Path.Combine(_data.Folder, "Documents"), Roots));
    }

    [Fact]
    public void Freeze_dumps_older_than_two_weeks_are_deleted_on_start()
    {
        var logs = Path.Combine(_data.Folder, "logs");
        Directory.CreateDirectory(logs);
        var now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        var old = Path.Combine(logs, "freeze-2026-10-01_20-00-00.dmp");
        var recent = Path.Combine(logs, "freeze-2026-11-10_20-00-00.dmp");
        var log = Path.Combine(logs, "console-2026-10-01.log");
        foreach (var file in new[] { old, recent, log })
        {
            File.WriteAllText(file, "x");
        }

        File.SetLastWriteTimeUtc(old, now.AddDays(-15));
        File.SetLastWriteTimeUtc(recent, now.AddDays(-5));
        File.SetLastWriteTimeUtc(log, now.AddDays(-30));

        Assert.Equal(1, HangWatchdog.DeleteOldDumps(logs, now));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(log), "only dumps are touched");
        Assert.Equal(0, HangWatchdog.DeleteOldDumps(Path.Combine(_data.Folder, "no such folder"), now));
    }

    /// <summary>Lets the test read the bytes after the locking stream has closed what it wrote to.</summary>
    private sealed class NonClosing(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
