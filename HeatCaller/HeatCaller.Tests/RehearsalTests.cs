using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// The rehearsal copy (HeatCaller --rehearsal): what is copied, what is
/// left behind, and what is switched off in the copy. Run on folders of the
/// test's own - Rehearsal.Prepare takes both ends as arguments - so nothing
/// here goes near %AppData% or %LocalAppData%.
/// </summary>
public sealed class RehearsalTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string Real => Path.Combine(_data.Folder, "real");

    private string Copy => Path.Combine(_data.Folder, "rehearsal", "Data");

    private void WriteReal(string relativePath, string text)
    {
        var path = Path.Combine(Real, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void The_first_rehearsal_copies_the_club_s_data_and_its_folders()
    {
        WriteReal("profiles.json", "[1]");
        WriteReal(Path.Combine("Music", "Victory Lane.mp3"), "not really an mp3");

        Assert.True(Rehearsal.Prepare(Real, Copy));

        Assert.Equal("[1]", File.ReadAllText(Path.Combine(Copy, "profiles.json")));
        Assert.True(File.Exists(Path.Combine(Copy, "Music", "Victory Lane.mp3")));
    }

    [Fact]
    public void A_race_the_real_app_was_running_the_crash_logs_and_the_undo_folder_stay_behind()
    {
        WriteReal("race-in-progress.json", "{}");
        WriteReal("crash.log", "boom");
        WriteReal("crash.old.log", "older boom");
        WriteReal(Path.Combine("undo-holding", "20260926-120000", "logo.png"), "held");
        WriteReal("events.json", "{}");

        Rehearsal.Prepare(Real, Copy);

        Assert.True(File.Exists(Path.Combine(Copy, "events.json")));
        Assert.False(File.Exists(Path.Combine(Copy, "race-in-progress.json")));
        Assert.False(File.Exists(Path.Combine(Copy, "crash.log")));
        Assert.False(File.Exists(Path.Combine(Copy, "crash.old.log")));
        Assert.False(Directory.Exists(Path.Combine(Copy, "undo-holding")));
    }

    [Fact]
    public void Publishing_and_PIN_emails_are_switched_off_in_the_copy_and_nothing_else_changes()
    {
        WriteReal("settings.json", """
            {
              "RelayEnabled": true,
              "RacerPinsEmailAutomatically": true,
              "RelayClub": "acme-rc",
              "LastHeatSize": 6
            }
            """);

        Rehearsal.Prepare(Real, Copy);

        var copied = JsonNode.Parse(File.ReadAllText(Path.Combine(Copy, "settings.json")))!.AsObject();

        Assert.False(copied["RelayEnabled"]!.GetValue<bool>());
        Assert.False(copied["RacerPinsEmailAutomatically"]!.GetValue<bool>());
        Assert.Equal("acme-rc", copied["RelayClub"]!.GetValue<string>());
        Assert.Equal(6, copied["LastHeatSize"]!.GetValue<int>());

        // The club's own settings are not touched.
        var real = JsonNode.Parse(File.ReadAllText(Path.Combine(Real, "settings.json")))!.AsObject();
        Assert.True(real["RelayEnabled"]!.GetValue<bool>());
    }

    [Fact]
    public void A_later_rehearsal_carries_on_from_its_own_copy()
    {
        WriteReal("profiles.json", "[1]");
        Assert.True(Rehearsal.Prepare(Real, Copy));

        // The rehearsal changes its copy; the club's data moves on too.
        File.WriteAllText(Path.Combine(Copy, "profiles.json"), "[1, 2]");
        WriteReal("profiles.json", "[1, 3]");

        Assert.False(Rehearsal.Prepare(Real, Copy));
        Assert.Equal("[1, 2]", File.ReadAllText(Path.Combine(Copy, "profiles.json")));
    }

    [Fact]
    public void A_club_with_no_data_yet_gets_an_empty_rehearsal()
    {
        Assert.True(Rehearsal.Prepare(Real, Copy));

        Assert.True(Directory.Exists(Copy));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Copy));
    }

    [Fact]
    public void Only_the_switch_starts_a_rehearsal_and_never_the_program_s_own_path()
    {
        Assert.True(Rehearsal.Wanted(new[] { @"C:\HeatCaller\HeatCaller.exe", "--rehearsal" }));
        Assert.True(Rehearsal.Wanted(new[] { @"C:\HeatCaller\HeatCaller.exe", "--REHEARSAL" }));
        Assert.False(Rehearsal.Wanted(new[] { @"C:\HeatCaller\HeatCaller.exe" }));
        Assert.False(Rehearsal.Wanted(new[] { "--rehearsal" }));
    }

    [Fact]
    public void The_real_app_is_not_a_rehearsal_and_keeps_its_Documents_folders()
    {
        Assert.False(AppPaths.IsRehearsal);

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HeatCaller Backups"),
            BackupService.DefaultBackupFolder);
    }
}
