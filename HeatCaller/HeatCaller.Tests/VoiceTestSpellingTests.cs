namespace HeatCaller.Tests;

/// <summary>
/// Voice Settings' Test buttons and the HeatCaller Preview send the words the
/// race sends (10 Nov 2026, bug hunt 16): "A-Main" respelt as the letter and,
/// for the online voices, names shortened. See
/// VoiceSettingsControl.TextTheRaceSpeaks.
/// </summary>
public class VoiceTestSpellingTests
{
    private static AnnouncementService Announcer(bool shorten)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Billy Smith" });

        return new AnnouncementService(profiles, VoiceSettingsStore.Load(), SoundEffectsStore.Load())
        {
            ShortenNamesForCloudVoices = () => shorten,
        };
    }

    [Fact]
    public void Kokoro_is_given_the_letter_and_full_names()
    {
        using var data = new TempAppData();
        var announcer = Announcer(shorten: true);

        Assert.Equal(
            "Billy Smith leads the Eh-Main!",
            VoiceSettingsControl.TextTheRaceSpeaks("Billy Smith leads the A-Main!", kokoro: true, announcer.TextForCloudVoice));
    }

    [Fact]
    public void An_online_voice_is_given_the_letter_and_the_short_names_the_race_sends()
    {
        using var data = new TempAppData();
        var announcer = Announcer(shorten: true);

        Assert.Equal(
            "Billy S leads the Eh-Main!",
            VoiceSettingsControl.TextTheRaceSpeaks("Billy Smith leads the A-Main!", kokoro: false, announcer.TextForCloudVoice));
    }

    [Fact]
    public void Without_an_announcer_the_letter_is_still_respelt()
    {
        Assert.Equal(
            "Sam leads the Eh-Main from Bill and Frank.",
            VoiceSettingsControl.TextTheRaceSpeaks("Sam leads the A-Main from Bill and Frank.", kokoro: false, null));
    }
}
