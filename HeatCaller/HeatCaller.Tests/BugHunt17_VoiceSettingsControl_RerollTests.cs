namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: with a race-calls model set, a race call ("Last lap!") is
/// saved under that model, but Re-roll Line only ever looked for it under
/// the main one - and refused every race call as "saved with a different
/// voice or different settings". It now finds the take under the race-calls
/// model and makes the new one there, where the race looks.
/// </summary>
public class BugHunt17_VoiceSettingsControl_RerollTests
{
    private static VoiceProfile WithRaceCallsOnFlash() => new()
    {
        Engine = VoiceEngine.ElevenLabs,
        ApiKey = "key",
        VoiceId = "v1",
        ModelId = "eleven_v3",
        RaceCallModelId = "eleven_flash_v2_5",
    };

    [Fact]
    public void A_race_call_saved_on_the_race_calls_model_can_be_re_rolled_there()
    {
        var profile = WithRaceCallsOnFlash();
        var asTheRaceSaidIt = AnnouncementService.ForRaceCall(profile, AnnouncementEventType.LastLap);
        var line = new VoiceLibrary.SavedLine(VoiceLibrary.KeyFor(asTheRaceSaidIt, "Last lap!"), "Last lap!", "Voice", DateTime.UtcNow, 100);

        // Looked for under the main model alone, it was refused.
        Assert.NotNull(VoiceSettingsControl.WhyNoReroll(profile, line, VoiceLibrary.KeyFor(profile, line.Text)));

        var found = VoiceSettingsControl.ProfileSavedUnder(profile, line);

        Assert.Equal("eleven_flash_v2_5", found.ModelId);
        Assert.Equal(line.Key, VoiceLibrary.KeyFor(found, line.Text));
        Assert.Null(VoiceSettingsControl.WhyNoReroll(found, line, VoiceLibrary.KeyFor(found, line.Text)));
    }

    [Fact]
    public void A_line_saved_on_the_main_model_is_re_rolled_on_the_main_model()
    {
        var profile = WithRaceCallsOnFlash();
        var line = new VoiceLibrary.SavedLine(VoiceLibrary.KeyFor(profile, "Green flag!"), "Green flag!", "Voice", DateTime.UtcNow, 100);

        Assert.Same(profile, VoiceSettingsControl.ProfileSavedUnder(profile, line));
    }

    [Fact]
    public void A_line_saved_with_neither_model_is_still_refused()
    {
        var profile = WithRaceCallsOnFlash();
        var line = new VoiceLibrary.SavedLine("someone-elses-key", "Last lap!", "Voice", DateTime.UtcNow, 100);

        var found = VoiceSettingsControl.ProfileSavedUnder(profile, line);

        Assert.Same(profile, found);
        Assert.NotNull(VoiceSettingsControl.WhyNoReroll(found, line, VoiceLibrary.KeyFor(found, line.Text)));
    }
}
