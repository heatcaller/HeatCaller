using System.IO.Compression;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, B33: a restore of a backup with no applied-soundscape
/// marker (made with the sound on lap times only) left this laptop's in
/// place, so the restored data played the old soundscape's theme music
/// and imaging.
/// </summary>
public sealed class BugHunt15_BackupSoundscapeTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string Backups => Path.Combine(_data.Folder, "backups");

    private string BackupWith(params (string Name, string Text)[] entries)
    {
        var zip = Path.Combine(_data.Folder, "other-laptop.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(text);
        }

        return zip;
    }

    [Fact]
    public void A_backup_with_no_soundscape_applied_clears_this_laptops_marker()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf(ThemeStore.ActiveFileName), Theme.BuiltInId("Arcade"));
        Assert.Equal(Theme.BuiltInId("Arcade"), ThemeStore.Load().ActiveThemeId);

        var zip = BackupWith(("settings.json", "{}"));

        var result = BackupService.RestoreBackup(zip, Backups);

        Assert.Empty(result.Failed);
        Assert.False(File.Exists(_data.PathOf(ThemeStore.ActiveFileName)));
        Assert.Null(ThemeStore.Load().ActiveThemeId);
    }

    [Fact]
    public void A_backup_with_its_own_soundscape_applied_brings_it()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf(ThemeStore.ActiveFileName), Theme.BuiltInId("Arcade"));

        var zip = BackupWith(
            ("settings.json", "{}"),
            (ThemeStore.ActiveFileName, Theme.BuiltInId("Podium")));

        var result = BackupService.RestoreBackup(zip, Backups);

        Assert.Empty(result.Failed);
        Assert.Equal(Theme.BuiltInId("Podium"), File.ReadAllText(_data.PathOf(ThemeStore.ActiveFileName)));
    }
}
