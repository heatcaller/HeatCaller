using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Backup &amp; Restore and Support Info (5 Nov 2026, bug hunt 11):
///
/// 1. Put Back Earlier Settings wrote a copy's settings.json back word for
///    word, so a copy made in Advanced mode, put back from Simple, restarted
///    HeatCaller in Advanced with no manager PIN asked - and brought back an
///    old manager PIN, an unticked PIN lock and old tokens. What a restore
///    keeps (KeptOnRestore) now stays as it is live; the tuning comes back.
///
/// 2. Support Info's "ElevenLabs key" line read AppSettings.ApiKey - the
///    retired Next Level Timing key - so a club with a working ElevenLabs
///    key (in the voice settings) read "not set".
/// </summary>
public sealed class BugHunt11_BackupExportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeatCallerBugHunt11", Guid.NewGuid().ToString("N"));

    public BugHunt11_BackupExportTests() => Directory.CreateDirectory(_folder);

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

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    private static string Json(AppSettings settings) => JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

    private AppSettings ReadSettings() => JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))!;

    // --- 1. Put Back keeps the mode, the PIN, its lock and the keys --------------------

    [Fact]
    public void Putting_back_an_Advanced_copy_from_Simple_keeps_Simple_the_new_PIN_and_its_lock()
    {
        var shipped = new AppSettings();

        // The copy: made in Advanced, before the lock was ticked and the PIN changed.
        File.WriteAllText(SettingsPath, Json(new AppSettings
        {
            SettingsMode = SettingsMode.Advanced,
            SettingsModeDecided = true,
            ManagerPin = "1111",
            AdvancedNeedsManagerPin = false,
            RelayToken = "old-token",
            RaceMinLapTimeMs = shipped.RaceMinLapTimeMs + 700,
        }));

        var zip = SettingsRestore.SaveCopy(_folder, new DateTime(2026, 11, 1, 20, 0, 0));

        // Now: Simple, locked, a new PIN and token, the tuning back to shipped.
        File.WriteAllText(SettingsPath, Json(new AppSettings
        {
            SettingsMode = SettingsMode.Simple,
            SettingsModeDecided = true,
            ManagerPin = "2222",
            AdvancedNeedsManagerPin = true,
            RelayToken = "new-token",
        }));

        SettingsRestore.PutBack(_folder, zip);
        var back = ReadSettings();

        Assert.Equal(SettingsMode.Simple, back.SettingsMode);
        Assert.Equal("2222", back.ManagerPin);
        Assert.True(back.AdvancedNeedsManagerPin);
        Assert.Equal("new-token", back.RelayToken);

        // What Put Back is for still comes back.
        Assert.Equal(shipped.RaceMinLapTimeMs + 700, back.RaceMinLapTimeMs);
    }

    [Fact]
    public void Putting_back_a_copy_with_no_live_settings_file_comes_back_in_Simple()
    {
        File.WriteAllText(SettingsPath, Json(new AppSettings { SettingsMode = SettingsMode.Advanced, SettingsModeDecided = true }));
        var zip = SettingsRestore.SaveCopy(_folder, new DateTime(2026, 11, 1, 20, 0, 0));

        File.Delete(SettingsPath);
        SettingsRestore.PutBack(_folder, zip);

        Assert.Equal(SettingsMode.Simple, ReadSettings().SettingsMode);
    }

    // --- 2. Support Info's ElevenLabs key line -----------------------------------------

    [Fact]
    public void The_support_summary_says_the_voice_settings_ElevenLabs_key_is_set()
    {
        // The retired Next Level Timing key is empty; the ElevenLabs key is in the voice settings.
        var text = SupportBundle.DescribeSettings(new AppSettings { ApiKey = string.Empty }, elevenLabsKeySet: true);

        Assert.Contains("ElevenLabs key:          set", text);
    }

    [Fact]
    public void A_leftover_Next_Level_Timing_key_is_not_reported_as_an_ElevenLabs_key()
    {
        var text = SupportBundle.DescribeSettings(new AppSettings { ApiKey = "nlt-SECRET" }, elevenLabsKeySet: false);

        Assert.Contains("ElevenLabs key:          not set", text);
        Assert.DoesNotContain("SECRET", text, StringComparison.Ordinal);
    }
}
