using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The settings table and the Simple / Advanced mode decision (1 Nov 2026,
/// Simple and Advanced mode, round 1). Every setting has to be put in a group
/// by someone - a new one fails here until it is - and the table has to name
/// only settings that exist.
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
            $"Not in SettingsTable.{file} - put each in ShownInSimple, HiddenInSimple, KeptOnRestore or Memory: {string.Join(", ", missing)}");
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
    public void The_mode_itself_is_never_hidden()
    {
        // Restore turns Simple back on itself, and the update check must not
        // count the mode it is about to set as something the club changed.
        Assert.Equal(SettingGroup.KeptOnRestore, SettingsTable.App[nameof(AppSettings.SettingsMode)]);
        Assert.Equal(SettingGroup.KeptOnRestore, SettingsTable.App[nameof(AppSettings.SettingsModeDecided)]);
    }

    [Fact]
    public void The_manager_pin_lock_survives_a_restore()
    {
        Assert.Equal(SettingGroup.KeptOnRestore, SettingsTable.App[nameof(AppSettings.AdvancedNeedsManagerPin)]);
        Assert.False(new AppSettings().AdvancedNeedsManagerPin);
    }

    [Fact]
    public void Only_the_manager_pin_itself_opens_advanced_mode()
    {
        Assert.True(ManagerPinForm.Matches("482913", "482913"));
        Assert.True(ManagerPinForm.Matches(" 482913 ", "482913"));
        Assert.False(ManagerPinForm.Matches("482914", "482913"));
        Assert.False(ManagerPinForm.Matches("48291", "482913"));
        Assert.False(ManagerPinForm.Matches(string.Empty, string.Empty));
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

    // --- A new settings file ---------------------------------------------------------

    [Fact]
    public void A_new_settings_file_is_simple_and_undecided()
    {
        var settings = new AppSettings();

        Assert.Equal(SettingsMode.Simple, settings.SettingsMode);
        Assert.False(settings.SettingsModeDecided);
    }

    [Fact]
    public void A_file_from_before_the_modes_reads_as_undecided()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"HasCompletedFirstRun\":true}")!;

        Assert.False(settings.SettingsModeDecided);
        Assert.Equal(SettingsMode.Simple, settings.SettingsMode);
    }

    // --- Nothing changed, something changed ------------------------------------------

    [Fact]
    public void Defaults_have_nothing_hidden_changed()
    {
        var changed = SettingsModes.ChangedHidden(
            new AppSettings(), VoiceProfile.CreateDefault(), new MusicSettings(), new AmbienceSettings(), new ImagingSettings());

        Assert.Empty(changed);
    }

    [Fact]
    public void A_hidden_setting_off_its_default_is_found()
    {
        var settings = new AppSettings { RaceMinLapTimeMs = new AppSettings().RaceMinLapTimeMs + 500 };

        Assert.Equal(new[] { nameof(AppSettings.RaceMinLapTimeMs) }, SettingsModes.ChangedHidden(settings));
    }

    [Fact]
    public void A_hidden_list_compares_by_what_is_in_it()
    {
        var settings = new AppSettings { NavOrder = new List<string>(new AppSettings().NavOrder) };

        Assert.Empty(SettingsModes.ChangedHidden(settings));

        settings.NavOrder.Add("Race Day");

        Assert.Equal(new[] { nameof(AppSettings.NavOrder) }, SettingsModes.ChangedHidden(settings));
    }

    [Fact]
    public void A_shown_setting_off_its_default_is_not_counted()
    {
        var settings = new AppSettings { RaceLapLimit = new AppSettings().RaceLapLimit + 3, Use24HourTime = !new AppSettings().Use24HourTime };

        Assert.Empty(SettingsModes.ChangedHidden(settings));
    }

    [Fact]
    public void Keys_and_memory_off_their_defaults_are_not_counted()
    {
        var settings = new AppSettings
        {
            RelayToken = "club-token",
            ManagerPin = "4321",
            LastTrackLayoutName = "Hairpin",
            FontScale = 1.25f,
        };

        Assert.Empty(SettingsModes.ChangedHidden(settings));
    }

    [Fact]
    public void The_other_files_say_which_they_are()
    {
        var voice = VoiceProfile.CreateDefault();
        voice.Stability += 0.1;

        var music = new MusicSettings { PauseDuringRaces = !new MusicSettings().PauseDuringRaces };

        Assert.Equal(
            new[] { "Voice: Stability", "Music: PauseDuringRaces" },
            SettingsModes.ChangedHidden(new AppSettings(), voice, music));
    }

    // --- Deciding once ---------------------------------------------------------------

    [Fact]
    public void A_new_install_is_simple_and_never_asks_what_changed()
    {
        var settings = new AppSettings { HasCompletedFirstRun = false };

        var decision = SettingsModes.Decide(settings, () => throw new InvalidOperationException("asked"));

        Assert.Equal(SettingsModes.Decision.NewInstall, decision);
        Assert.Equal(SettingsMode.Simple, settings.SettingsMode);
        Assert.True(settings.SettingsModeDecided);
        Assert.Null(SettingsModes.UpdateNote(decision));
    }

    [Fact]
    public void An_update_with_nothing_hidden_changed_goes_to_simple()
    {
        var settings = new AppSettings { HasCompletedFirstRun = true };

        var decision = SettingsModes.Decide(settings, Array.Empty<string>);

        Assert.Equal(SettingsModes.Decision.UpdatedToSimple, decision);
        Assert.Equal(SettingsMode.Simple, settings.SettingsMode);
        Assert.True(settings.SettingsModeDecided);
        Assert.NotNull(SettingsModes.UpdateNote(decision));
    }

    [Fact]
    public void An_update_with_something_hidden_changed_stays_advanced()
    {
        var settings = new AppSettings { HasCompletedFirstRun = true };

        var decision = SettingsModes.Decide(settings, () => new[] { nameof(AppSettings.RaceMinLapTimeMs) });

        Assert.Equal(SettingsModes.Decision.UpdatedToAdvanced, decision);
        Assert.Equal(SettingsMode.Advanced, settings.SettingsMode);
        Assert.Contains("Advanced", SettingsModes.UpdateNote(decision));
    }

    [Fact]
    public void It_is_decided_only_once()
    {
        var settings = new AppSettings { HasCompletedFirstRun = true, SettingsModeDecided = true, SettingsMode = SettingsMode.Simple };

        var decision = SettingsModes.Decide(settings, () => new[] { nameof(AppSettings.RaceMinLapTimeMs) });

        Assert.Equal(SettingsModes.Decision.AlreadyDecided, decision);
        Assert.Equal(SettingsMode.Simple, settings.SettingsMode);
        Assert.Null(SettingsModes.UpdateNote(decision));
    }

    [Fact]
    public void The_mode_survives_a_save_and_load()
    {
        var settings = new AppSettings { SettingsMode = SettingsMode.Advanced, SettingsModeDecided = true };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(SettingsMode.Advanced, back.SettingsMode);
        Assert.True(back.SettingsModeDecided);
    }
}
