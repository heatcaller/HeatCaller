using System.IO.Compression;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026), B34: Put Back Earlier Settings' copies are
/// dated by the time in their names. A full-backup restore writes every
/// copy fresh, so their modified times all read the restore's minute -
/// listing and pruning by those mixed the copies up.
/// </summary>
public sealed class BugHunt15_SettingsCopiesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeatCallerBugHunt15Settings", Guid.NewGuid().ToString("N"));

    public BugHunt15_SettingsCopiesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A copy called <paramref name="name"/>, its modified time set to <paramref name="written"/>, as a restore leaves it.</summary>
    private string MakeCopy(string name, DateTime written)
    {
        var folder = SettingsRestore.CopiesFolder(_folder);
        Directory.CreateDirectory(folder);
        var zip = Path.Combine(folder, name);

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("settings.json").Open());
            writer.Write("{}");
        }

        File.SetLastWriteTime(zip, written);
        return zip;
    }

    [Fact]
    public void Restored_copies_are_dated_and_ordered_by_their_names()
    {
        // Extracted oldest-last, all within the restore's minute.
        var restored = new DateTime(2026, 11, 9, 12, 0, 0);
        var newest = MakeCopy("settings 2026-11-02 08-30-00.zip", restored);
        var middle = MakeCopy("settings 2026-10-20 19-15-45.zip", restored.AddSeconds(1));
        var oldest = MakeCopy("settings 2026-10-01 07-00-00.zip", restored.AddSeconds(2));

        var copies = SettingsRestore.Copies(_folder);

        Assert.Equal(new[] { newest, middle, oldest }, copies.Select(copy => copy.Path));
        Assert.Equal(new DateTime(2026, 11, 2, 8, 30, 0), copies[0].When);
        Assert.Equal(new DateTime(2026, 10, 20, 19, 15, 45), copies[1].When);
        Assert.Equal(new DateTime(2026, 10, 1, 7, 0, 0), copies[2].When);
    }

    [Fact]
    public void A_numbered_copy_from_the_same_second_sorts_after_its_first()
    {
        var restored = new DateTime(2026, 11, 9, 12, 0, 0);
        var third = MakeCopy("settings 2026-11-02 08-30-00 (3).zip", restored);
        var first = MakeCopy("settings 2026-11-02 08-30-00.zip", restored.AddSeconds(2));
        var second = MakeCopy("settings 2026-11-02 08-30-00 (2).zip", restored.AddSeconds(1));

        var copies = SettingsRestore.Copies(_folder);

        Assert.Equal(new[] { third, second, first }, copies.Select(copy => copy.Path));
        Assert.All(copies, copy => Assert.Equal(new DateTime(2026, 11, 2, 8, 30, 0), copy.When));
    }

    [Fact]
    public void A_copy_without_a_readable_time_falls_back_to_its_modified_time()
    {
        var written = new DateTime(2026, 10, 15, 9, 0, 0);
        var odd = MakeCopy("settings from the old laptop.zip", written);

        var copy = Assert.Single(SettingsRestore.Copies(_folder));

        Assert.Equal(odd, copy.Path);
        Assert.Equal(written, copy.When);
    }

    [Fact]
    public void Pruning_after_a_restore_deletes_the_oldest_by_name()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{}");

        // Ten copies, extracted newest-first, so the oldest has the newest modified time.
        var restored = new DateTime(2026, 11, 9, 12, 0, 0);
        var oldest = "";
        for (var i = 0; i < SettingsRestore.CopiesKept; i++)
        {
            oldest = MakeCopy($"settings 2026-10-{20 - i:00} 10-00-00.zip", restored.AddSeconds(i));
        }

        SettingsRestore.SaveCopy(_folder, new DateTime(2026, 11, 9, 13, 0, 0));

        Assert.False(File.Exists(oldest), "the oldest copy was kept");
        Assert.True(File.Exists(Path.Combine(SettingsRestore.CopiesFolder(_folder), "settings 2026-10-20 10-00-00.zip")), "the newest restored copy was pruned");
        Assert.Equal(SettingsRestore.CopiesKept, SettingsRestore.Copies(_folder).Count);
    }
}
