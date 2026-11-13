using System.IO.Compression;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): SaveCopy pruned the copy it had just made when
/// ten copies were already named later than the clock read (a clock set
/// back, a dead clock battery) - so the restore ran with nothing to put back.
/// </summary>
public sealed class BugHunt17_SettingsRestoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeatCallerBugHunt17Settings", Guid.NewGuid().ToString("N"));

    public BugHunt17_SettingsRestoreTests() => Directory.CreateDirectory(_folder);

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

    private string MakeCopy(string name)
    {
        var folder = SettingsRestore.CopiesFolder(_folder);
        Directory.CreateDirectory(folder);
        var zip = Path.Combine(folder, name);

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("settings.json").Open());
            writer.Write("{}");
        }

        return zip;
    }

    [Fact]
    public void A_copy_made_with_the_clock_set_back_is_not_pruned_on_the_spot()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{}");

        // Ten copies, every one named later than the clock reads now.
        for (var i = 0; i < SettingsRestore.CopiesKept; i++)
        {
            MakeCopy($"settings 2026-11-{i + 1:00} 10-00-00.zip");
        }

        var zip = SettingsRestore.SaveCopy(_folder, new DateTime(2020, 1, 1, 0, 0, 0));

        Assert.True(File.Exists(zip), "the copy just made was deleted");
    }

    [Fact]
    public void Put_back_keeps_both_the_picked_copy_and_the_new_one()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{}");

        var picked = "";
        for (var i = 0; i < SettingsRestore.CopiesKept; i++)
        {
            picked = MakeCopy($"settings 2026-11-{20 - i:00} 10-00-00.zip");
        }

        var saved = SettingsRestore.PutBackWithCopy(_folder, picked, new DateTime(2020, 1, 1, 0, 0, 0));

        Assert.True(File.Exists(saved), "the copy of the settings as they were was deleted");
        Assert.True(File.Exists(picked), "the copy picked was deleted");
    }
}
