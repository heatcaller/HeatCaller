namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: a race-call row's Test button spoke on the main model, while
/// the race says that call on the race-calls model when one is set
/// (AnnouncementService.ForRaceCall). The take heard, paid for and saved was
/// one the race never plays. Test now uses the model the race will.
/// </summary>
public class BugHunt17_VoiceSettingsControlTests
{
    private static VoiceProfile WithRaceCallsOnFlash() => new()
    {
        Engine = VoiceEngine.ElevenLabs,
        ApiKey = "key",
        VoiceId = "v1",
        ModelId = "eleven_v3",
        RaceCallModelId = "eleven_flash_v2_5",
        Speed = 1.0,
    };

    [Fact]
    public void A_race_call_is_tested_on_the_race_calls_model_at_its_own_speed()
    {
        var profile = WithRaceCallsOnFlash();

        var test = VoiceSettingsControl.TestProfileFor(profile, AnnouncementEventType.LastLap, 0.9);

        Assert.Equal("eleven_flash_v2_5", test.ModelId);
        Assert.Equal(0.9, test.Speed, 3);

        // The request the race makes: the same key, so the take tested is
        // the take played.
        var live = AnnouncementService.ForRaceCall(profile, AnnouncementEventType.LastLap)
            .CloneForDelivery(profile.Stability, profile.Style, 0.9);
        var words = live.SanitiseForModel("[excited] Last lap!");

        Assert.Equal("Last lap!", words.Trim());
        Assert.Equal(VoiceLibrary.KeyFor(live, words), VoiceLibrary.KeyFor(test, test.SanitiseForModel("[excited] Last lap!")));
    }

    [Fact]
    public void A_flag_is_still_tested_on_the_main_model()
    {
        var profile = WithRaceCallsOnFlash();

        var test = VoiceSettingsControl.TestProfileFor(profile, AnnouncementEventType.YellowFlag, 1.0);

        Assert.Equal("eleven_v3", test.ModelId);
        Assert.Equal(VoiceLibrary.KeyFor(profile, "Yellow flag!"), VoiceLibrary.KeyFor(test, "Yellow flag!"));
    }

    [Fact]
    public void With_no_race_calls_model_a_race_call_is_tested_on_the_main_model()
    {
        var profile = WithRaceCallsOnFlash();
        profile.RaceCallModelId = string.Empty;

        Assert.Equal("eleven_v3", VoiceSettingsControl.TestProfileFor(profile, AnnouncementEventType.LastLap, 1.0).ModelId);
    }
}
