using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): a locked backup that is cut short, damaged or
/// locked by a newer HeatCaller used to be reported as a wrong password,
/// even for the right one. Only a wrong password is now said as that; the
/// restore's own unlocking gives the real reason.
/// </summary>
public sealed class BugHunt24_BackupCryptoTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string LockedFile(string password)
    {
        Directory.CreateDirectory(_data.Folder);
        var path = Path.Combine(_data.Folder, "HeatCaller-Backup-" + Guid.NewGuid().ToString("N")[..8] + ".zip");

        using (var locked = BackupCrypto.Encrypt(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None), password, iterations: 10_000))
        {
            locked.Write(Encoding.UTF8.GetBytes("twenty bytes of data"));
        }

        return path;
    }

    private static int HeaderLength => BackupCrypto.Magic.Length + 1 + 4 + 16 + 4 + 4;

    [Fact]
    public void BugHunt24_an_intact_backup_still_tells_the_right_password_from_a_wrong_one()
    {
        var path = LockedFile("right");

        Assert.True(BackupCrypto.PasswordOpens(path, "right"));
        Assert.False(BackupCrypto.PasswordOpens(path, "wrong"));
        Assert.False(BackupCrypto.PasswordOpens(path, string.Empty));
    }

    [Fact]
    public void BugHunt24_a_cut_short_backup_is_not_called_a_wrong_password()
    {
        var path = LockedFile("right");
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(HeaderLength + 10)]);

        Assert.True(BackupService.IsLocked(path));
        Assert.True(BackupCrypto.PasswordOpens(path, "right"));
        Assert.Equal("right", BackupRestoreControl.SavedPasswordThatOpens(path, "right"));

        // The restore then refuses it with the real reason, nothing touched.
        Directory.CreateDirectory(AppPaths.Root);
        var refused = Assert.Throws<InvalidDataException>(() => BackupService.RestoreBackup(path, password: "right"));
        Assert.Contains("cut short", refused.Message);
    }

    [Fact]
    public void BugHunt24_a_backup_locked_by_a_newer_HeatCaller_is_not_called_a_wrong_password()
    {
        var path = LockedFile("right");
        var bytes = File.ReadAllBytes(path);
        bytes[BackupCrypto.Magic.Length] = 2;
        File.WriteAllBytes(path, bytes);

        Assert.True(BackupCrypto.PasswordOpens(path, "right"));

        Directory.CreateDirectory(AppPaths.Root);
        var refused = Assert.Throws<InvalidDataException>(() => BackupService.RestoreBackup(path, password: "right"));
        Assert.Contains("newer HeatCaller", refused.Message);
    }

    [Fact]
    public void BugHunt24_a_backup_with_a_damaged_lock_is_not_called_a_wrong_password()
    {
        var path = LockedFile("right");
        var bytes = File.ReadAllBytes(path);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(BackupCrypto.Magic.Length + 1, 4), 1);
        File.WriteAllBytes(path, bytes);

        Assert.True(BackupCrypto.PasswordOpens(path, "right"));

        Directory.CreateDirectory(AppPaths.Root);
        var refused = Assert.Throws<InvalidDataException>(() => BackupService.RestoreBackup(path, password: "right"));
        Assert.Contains("lock is damaged", refused.Message);
    }
}
