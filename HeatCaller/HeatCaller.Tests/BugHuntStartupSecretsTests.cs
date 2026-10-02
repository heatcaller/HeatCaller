namespace HeatCaller.Tests;

/// <summary>
/// settings.json that cannot be read at startup.
///
/// It holds every sealed key - the relay token, the PINs, the email and
/// backup passwords and the badge key every printed QR check-in depends on -
/// so a file that was only locked for a moment at launch (a backup or sync
/// tool) must never be written over with defaults that session. Damaged
/// content that could be copied aside is still started afresh, as before.
/// </summary>
public sealed class BugHuntStartupSecretsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Settings_locked_at_startup_are_not_written_over()
    {
        Assert.True(new AppSettings { RaceWarmUpSeconds = 45 }.Save());
        var before = File.ReadAllText(_data.PathOf("settings.json"));

        AppSettings tonight;
        using (new FileStream(_data.PathOf("settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = AppSettings.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        Assert.Equal(0, tonight.RaceWarmUpSeconds);

        // What the first-run wizard does at once with the defaults.
        tonight.HasCompletedFirstRun = true;
        Assert.False(tonight.Save());

        Assert.Equal(before, File.ReadAllText(_data.PathOf("settings.json")));
        Assert.Equal(45, AppSettings.Load().RaceWarmUpSeconds);
    }

    [Fact]
    public void Damaged_settings_copied_aside_still_start_afresh_and_save()
    {
        File.WriteAllText(_data.PathOf("settings.json"), "{ this is not json");

        var settings = AppSettings.Load();

        Assert.False(settings.LoadedUnreadable);
        Assert.NotEmpty(Directory.GetFiles(AppPaths.Root, "settings.json.unreadable-*"));
        Assert.True(settings.Save());
    }

    [Fact]
    public void A_readable_settings_file_is_not_held()
    {
        Assert.True(new AppSettings { RaceWarmUpSeconds = 30 }.Save());

        var settings = AppSettings.Load();

        Assert.False(settings.LoadedUnreadable);
        Assert.Equal(30, settings.RaceWarmUpSeconds);
        Assert.True(settings.Save());
    }
}
