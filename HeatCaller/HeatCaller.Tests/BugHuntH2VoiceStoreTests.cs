namespace HeatCaller.Tests;

/// <summary>
/// voice-settings.json that cannot be read at startup.
///
/// It holds the only copy on this PC of the sealed ElevenLabs key, the
/// chosen voice and every announcement line the club edited, so a file that
/// was only locked for a moment at launch (a backup or sync tool) must never
/// be written over with defaults that session. Damaged content that could
/// be copied aside is still started afresh, as before.
/// </summary>
public sealed class BugHuntH2VoiceStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Voice_settings_locked_at_startup_are_not_written_over()
    {
        var first = VoiceSettingsStore.Load();
        var profile = VoiceProfile.CreateDefault();
        profile.VoiceName = "Club Voice";
        Assert.True(first.Save(profile));
        var before = File.ReadAllText(_data.PathOf("voice-settings.json"));

        VoiceSettingsStore tonight;
        using (new FileStream(_data.PathOf("voice-settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = VoiceSettingsStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        Assert.NotEqual("Club Voice", tonight.Current.VoiceName);

        // What the Mixer does when the announcer volume is nudged.
        Assert.False(tonight.Save(tonight.Current));

        Assert.Equal(before, File.ReadAllText(_data.PathOf("voice-settings.json")));
        Assert.Equal("Club Voice", VoiceSettingsStore.Load().Current.VoiceName);
    }

    [Fact]
    public void Damaged_voice_settings_copied_aside_still_start_afresh_and_save()
    {
        File.WriteAllText(_data.PathOf("voice-settings.json"), "{ this is not json");

        var store = VoiceSettingsStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.NotEmpty(Directory.GetFiles(AppPaths.Root, "voice-settings.json.unreadable-*"));
        Assert.True(store.Save(store.Current));
    }

    [Fact]
    public void A_readable_voice_settings_file_is_not_held()
    {
        var profile = VoiceProfile.CreateDefault();
        profile.VoiceName = "Club Voice";
        Assert.True(VoiceSettingsStore.Load().Save(profile));

        var store = VoiceSettingsStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.Equal("Club Voice", store.Current.VoiceName);
    }
}
