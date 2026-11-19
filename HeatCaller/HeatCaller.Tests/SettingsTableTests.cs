using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The settings table (1 Nov 2026). Every setting has to be put in a group
/// by someone - a new one fails here until it is - and the table has to name
/// only settings that exist. Restore Default Settings reads it.
/// </summary>
public sealed class SettingsTableTests
{
    // --- Every setting is in a group -------------------------------------------------

    public static TheoryData<string> Files => new() { "App", "Voice", "Music", "Ambience", "Imaging" };

    private static (Type Type, IReadOnlyDictionary<string, SettingGroup> Table) For(string file) => file switch
    {
        "App" => (typeof(AppSettings), SettingsTable.App),
        "Voice" => (typeof(VoiceProfile), SettingsTable.Voice),
        "Music" => (typeof(MusicSettings), SettingsTable.Music),
        "Ambience" => (typeof(AmbienceSettings), SettingsTable.Ambience),
        "Imaging" => (typeof(ImagingSettings), SettingsTable.Imaging),
        _ => throw new ArgumentOutOfRangeException(nameof(file)),
    };

    /// <summary>
    /// The settings a file saves, or that a session reads off it: every
    /// public instance property that can be set, the [JsonIgnore] secrets
    /// included. One with only a getter (EffectiveEngine, CoHostVoice) is
    /// worked out from the others, not a setting of its own.
    /// </summary>
    private static IEnumerable<string> Settings(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.SetMethod is not null && p.GetIndexParameters().Length == 0)
        .Select(p => p.Name);

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_setting_is_in_a_group(string file)
    {
        var (type, table) = For(file);

        var missing = Settings(type).Where(name => !table.ContainsKey(name)).OrderBy(n => n).ToList();

        Assert.True(missing.Count == 0,
            $"Not in SettingsTable.{file} - put each in Everyday, Tuning, KeptOnRestore or Memory: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void The_table_names_only_settings_that_exist(string file)
    {
        var (type, table) = For(file);
        var real = Settings(type).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(table.Keys, name => !real.Contains(name));
    }

    [Fact]
    public void Keys_accounts_and_the_decoder_port_are_kept()
    {
        foreach (var name in new[]
                 {
                     nameof(AppSettings.RelayClub), nameof(AppSettings.RelayToken), nameof(AppSettings.ManagerPin),
                     nameof(AppSettings.BackupPassword), nameof(AppSettings.SmtpPassword), nameof(AppSettings.ILapPort),
                     nameof(AppSettings.ILapBaud), nameof(AppSettings.LastConnectionType), nameof(AppSettings.HasCompletedFirstRun),
                 })
        {
            Assert.Equal(SettingGroup.KeptOnRestore, SettingsTable.App[name]);
        }

        Assert.Equal(SettingGroup.KeptOnRestore, SettingsTable.Voice[nameof(VoiceProfile.ApiKey)]);
    }
}
