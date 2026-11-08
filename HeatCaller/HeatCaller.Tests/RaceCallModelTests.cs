namespace HeatCaller.Tests;

/// <summary>
/// The race calls on a model of their own (8 Nov 2026). On a 5.5 s track v3
/// took 1.8-3.3 s to render a call where Flash took under 1.5, and the lap
/// calls said in time fell from 6 of 14 to 3 of 18. See
/// VoiceProfile.RaceCallModelId and AnnouncementService.ForRaceCall.
/// </summary>
public sealed class RaceCallModelTests
{
    private static VoiceProfile OnV3(string raceModel) => new()
    {
        Engine = VoiceEngine.ElevenLabs,
        ApiKey = "key",
        VoiceId = "voice",
        ModelId = "eleven_v3",
        RaceCallModelId = raceModel,
    };

    [Theory]
    [InlineData(AnnouncementEventType.FastLap)]
    [InlineData(AnnouncementEventType.LapTime)]
    [InlineData(AnnouncementEventType.LeaderChange)]
    [InlineData(AnnouncementEventType.CloseBattle)]
    [InlineData(AnnouncementEventType.PositionReport)]
    [InlineData(AnnouncementEventType.LastLap)]
    [InlineData(AnnouncementEventType.FinalMinute)]
    [InlineData(AnnouncementEventType.MinutesLeft)]
    public void A_race_call_renders_on_the_race_calls_model(AnnouncementEventType call)
    {
        var profile = AnnouncementService.ForRaceCall(OnV3("eleven_flash_v2_5"), call);

        Assert.Equal("eleven_flash_v2_5", profile.ModelId);
        Assert.Equal(VoiceEngine.ElevenLabs, profile.EffectiveEngine);
        Assert.Equal("voice", profile.VoiceId);
    }

    [Theory]
    [InlineData(AnnouncementEventType.Reminder)]
    [InlineData(AnnouncementEventType.RaceStarted)]
    [InlineData(AnnouncementEventType.YellowFlag)]
    [InlineData(AnnouncementEventType.RaceCompleted)]
    [InlineData(AnnouncementEventType.GridCall)]
    [InlineData(AnnouncementEventType.AchievementUnlocked)]
    [InlineData(AnnouncementEventType.TrackRecord)]
    public void The_show_flags_and_instructions_stay_on_the_model(AnnouncementEventType call)
    {
        var v3 = OnV3("eleven_flash_v2_5");

        Assert.Same(v3, AnnouncementService.ForRaceCall(v3, call));
    }

    [Fact]
    public void With_no_race_calls_model_set_nothing_changes()
    {
        var v3 = OnV3(string.Empty);

        Assert.Equal("eleven_v3", v3.RaceCallModel);
        Assert.Same(v3, AnnouncementService.ForRaceCall(v3, AnnouncementEventType.FastLap));
    }

    [Fact]
    public void A_line_from_outside_the_queue_stays_on_the_model()
    {
        var v3 = OnV3("eleven_flash_v2_5");

        Assert.Same(v3, AnnouncementService.ForRaceCall(v3, null));
    }

    [Fact]
    public void Only_elevenlabs_has_a_race_calls_model()
    {
        var kokoro = OnV3("eleven_flash_v2_5");
        kokoro.Engine = VoiceEngine.Kokoro;

        Assert.Same(kokoro, AnnouncementService.ForRaceCall(kokoro, AnnouncementEventType.FastLap));
    }

    [Fact]
    public void A_flash_race_call_has_its_audio_tags_taken_out()
    {
        var flash = AnnouncementService.ForRaceCall(OnV3("eleven_flash_v2_5"), AnnouncementEventType.FastLap);

        Assert.Equal("Best lap of the race!", flash.SanitiseForModel("[excited] Best lap of the race!").Trim());
    }

    [Fact]
    public void Kokoro_standing_in_for_a_flash_race_call_matches_flash()
    {
        var flash = AnnouncementService.ForRaceCall(OnV3("eleven_flash_v2_5"), AnnouncementEventType.FastLap);

        Assert.Equal(KokoroLoudness.StandardDb, KokoroLoudness.TargetDbFor(flash));
    }

    // --- Wiring -------------------------------------------------------------

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find check.cmd.");
    }

    [Fact]
    public void Every_queued_line_is_rendered_through_it_and_the_page_and_styles_keep_it()
    {
        Assert.Contains("var currentProfile = ForRaceCall(_voiceStore.Current, _dequeuedCall.Value);",
            Source("AnnouncementService.VoiceQueue.cs"), StringComparison.Ordinal);
        Assert.Contains("_dequeuedCall.Value = work is null ? null : dequeuedType;",
            Source("AnnouncementService.VoiceQueue.cs"), StringComparison.Ordinal);
        Assert.Contains("RaceCallModelId = _cmbRaceCallModel.SelectedItem is string raceModel",
            Source("VoiceSettingsControl.cs"), StringComparison.Ordinal);
        Assert.Contains("voiceCopy.RaceCallModelId = current.RaceCallModelId;",
            Source("ThemeStore.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_setting_is_saved_with_the_profile()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(OnV3("eleven_flash_v2_5"));
        var back = System.Text.Json.JsonSerializer.Deserialize<VoiceProfile>(json)!;

        Assert.Equal("eleven_flash_v2_5", back.RaceCallModelId);
    }
}
