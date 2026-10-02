namespace HeatCaller.Tests;

/// <summary>
/// Cloud copies of a backup in the rehearsal copy (HeatCaller --rehearsal).
/// The rehearsal finds the same sync folders as the real app, so it must
/// neither copy its practice backup into the club's cloud folder nor prune
/// the club's automatic backups there. Run on folders of the test's own,
/// standing in for the sync folder.
/// </summary>
public sealed class BugHuntCloudBackupTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string Local => Path.Combine(_data.Folder, "local");

    private string Cloud => Path.Combine(_data.Folder, "cloud", CloudBackup.FolderName);

    private static string AutoName(int day) => $"HeatCaller-AutoBackup-2026-09-{day:00}_20-00-00.zip";

    /// <summary>The club's cloud folder holding <paramref name="count"/> automatic backups, and a newer local one to copy.</summary>
    private string Arrange(int count)
    {
        Directory.CreateDirectory(Cloud);
        for (var day = 1; day <= count; day++)
        {
            File.WriteAllText(Path.Combine(Cloud, AutoName(day)), "real");
        }

        Directory.CreateDirectory(Local);
        var backup = Path.Combine(Local, AutoName(28));
        File.WriteAllText(backup, "practice");
        return backup;
    }

    private IReadOnlyList<CloudBackup.Target> Targets =>
        new[] { new CloudBackup.Target(CloudService.OneDrive, "OneDrive", Cloud) };

    [Fact]
    public void A_rehearsal_copies_nothing_to_the_cloud_and_prunes_nothing_there()
    {
        var backup = Arrange(3);

        var results = CloudBackup.CopyToTargets(backup, Targets, keepAutomatic: 3, isRehearsal: true);

        Assert.Single(results);
        Assert.StartsWith("Not copied to OneDrive", results[0]);
        Assert.False(File.Exists(Path.Combine(Cloud, AutoName(28))));
        for (var day = 1; day <= 3; day++)
        {
            Assert.True(File.Exists(Path.Combine(Cloud, AutoName(day))));
        }
    }

    [Fact]
    public void The_real_app_still_copies_and_prunes()
    {
        var backup = Arrange(3);

        var results = CloudBackup.CopyToTargets(backup, Targets, keepAutomatic: 3, isRehearsal: false);

        Assert.Equal(new[] { "Copied to OneDrive." }, results);
        Assert.True(File.Exists(Path.Combine(Cloud, AutoName(28))));
        Assert.False(File.Exists(Path.Combine(Cloud, AutoName(1))));
        Assert.True(File.Exists(Path.Combine(Cloud, AutoName(2))));
    }
}
