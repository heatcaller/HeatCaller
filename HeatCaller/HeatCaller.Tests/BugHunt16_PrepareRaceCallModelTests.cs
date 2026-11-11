namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16: Prepare Lines rendered every fixed line on the main model,
/// but a race call (Fastest lap!, Last lap!) is spoken live on the race-calls
/// model when one is set (AnnouncementService.ForRaceCall). The saved-voice
/// key has the model in it, so the prepared take was paid for and never
/// found. The plan now uses the model, and the words, the race will use.
/// </summary>
public class BugHunt16_PrepareRaceCallModelTests
{
    [Fact]
    public void A_race_call_is_prepared_on_the_race_calls_model_and_a_flag_on_the_main_one()
    {
        using var data = new TempAppData();

        var voice = VoiceSettingsStore.Load();
        var profile = voice.Current;
        profile.Engine = VoiceEngine.ElevenLabs;
        profile.ApiKey = "key";
        profile.VoiceId = "voice";
        profile.ModelId = "eleven_v3";
        profile.RaceCallModelId = "eleven_flash_v2_5";

        profile.Events.Clear();
        profile.Events[nameof(AnnouncementEventType.FastLap)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Templates = { "[excited] Fastest lap!" },
        };
        profile.Events[nameof(AnnouncementEventType.YellowFlag)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Templates = { "[urgent] Yellow flag!" },
        };

        var announcer = new AnnouncementService(ProfileStore.Load(), voice, SoundEffectsStore.Load());
        var plan = announcer.PlanLinesToPrepare();

        var fastLap = Assert.Single(plan, l => l.Text.Contains("Fastest lap", StringComparison.Ordinal));
        var yellow = Assert.Single(plan, l => l.Text.Contains("Yellow flag", StringComparison.Ordinal));

        // The race call: on Flash, with the tag Flash can't say taken out -
        // the same request the live path makes.
        Assert.Equal("eleven_flash_v2_5", fastLap.Profile.ModelId);
        Assert.Equal("Fastest lap!", fastLap.Text.Trim());

        var live = AnnouncementService.ForRaceCall(profile, AnnouncementEventType.FastLap);
        var liveText = live.SanitiseForModel("[excited] Fastest lap!".TidyTemplate());
        Assert.Equal(VoiceLibrary.KeyFor(live, liveText), VoiceLibrary.KeyFor(fastLap.Profile, fastLap.Text));

        // A flag is not a race call: it stays on v3 and keeps its tag.
        Assert.Equal("eleven_v3", yellow.Profile.ModelId);
        Assert.Contains("[urgent]", yellow.Text, StringComparison.Ordinal);
    }
}
