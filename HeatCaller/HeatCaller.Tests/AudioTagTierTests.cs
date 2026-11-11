using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// The v4 rework (10 Nov 2026): [shouting] and [excited] only on the calls
/// that earn them (AudioTagTiers), one tag per call, and every voice moved
/// to eleven_v4_turbo.
/// </summary>
public sealed class AudioTagTierTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly Regex Loud = new(@"\[(excited|shouting)\]", RegexOptions.Compiled);

    private static IEnumerable<(string Where, AnnouncementEventType Type, string Line)> Lines(string where, VoiceProfile voice)
    {
        foreach (var (key, config) in voice.Events)
        {
            var type = Enum.Parse<AnnouncementEventType>(key);

            foreach (var line in config.EffectiveTemplates)
            {
                yield return (where, type, line);
            }
        }
    }

    public static IEnumerable<object[]> Profiles()
    {
        yield return new object[] { "defaults" };

        foreach (var theme in Theme.CreateBuiltInDefaults())
        {
            yield return new object[] { theme.Name };
        }
    }

    private static VoiceProfile ProfileFor(string name) =>
        name == "defaults"
            ? VoiceProfile.CreateDefault()
            : Theme.CreateBuiltInDefaults().Single(t => t.Name == name).Voice;

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_loud_tag_is_only_ever_the_one_its_call_earns(string name)
    {
        foreach (var (where, type, line) in Lines(name, ProfileFor(name)))
        {
            var want = AudioTagTiers.For(type);

            foreach (Match tag in Loud.Matches(line))
            {
                Assert.True(tag.Value == want, $"{where} {type}: \"{line}\" has {tag.Value}, the call takes {want ?? "no loud tag"}");
            }
        }
    }

    [Fact]
    public void Every_built_in_line_of_a_loud_call_takes_its_tag_so_a_moment_never_comes_out_flat()
    {
        // Only the defaults: a soundscape like Podium is calm on purpose and
        // may leave a line untagged, but never gives it the wrong tag.
        foreach (var (where, type, line) in Lines("defaults", VoiceProfile.CreateDefault()))
        {
            if (AudioTagTiers.For(type) is { } want && !line.StartsWith('['))
            {
                Assert.Fail($"{where} {type}: \"{line}\" should open on {want}");
            }

            if (AudioTagTiers.For(type) is { } tag && line.StartsWith('[') && Loud.IsMatch(line))
            {
                Assert.StartsWith(tag, line);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_drivers_own_bests_are_never_lifted_but_keep_their_character_tags(string name)
    {
        var characterTags = 0;

        foreach (var (where, type, line) in Lines(name, ProfileFor(name)))
        {
            if (AudioTagTiers.Unlifted.Contains(type))
            {
                Assert.Null(AudioTagTiers.For(type));
                Assert.False(Loud.IsMatch(line), $"{where} {type}: \"{line}\" should have no [excited] or [shouting]");

                if (line.Contains("[cheerful]", StringComparison.Ordinal) || line.Contains("[awe]", StringComparison.Ordinal))
                {
                    characterTags++;
                }
            }
        }

        // The defaults' personal-best and achievement lines open on
        // [cheerful] - kept, not stripped with the loud tags (11 Nov 2026).
        if (name == "defaults")
        {
            Assert.True(characterTags > 0, "the [cheerful] lines are kept");
        }
    }

    [Fact]
    public void A_file_on_version_4_only_loses_the_loud_tags_on_a_drivers_own_bests()
    {
        var saved = VoiceProfile.CreateDefault();
        saved.TemplateVersion = 4;
        saved.ModelId = "eleven_v3";
        saved.Events[nameof(AnnouncementEventType.PersonalBestLap)].Templates =
            new List<string> { "[excited] Our own PB line for {driver}!", "[cheerful] {driver} again." };
        saved.Events[nameof(AnnouncementEventType.AchievementUnlocked)].Templates = new List<string> { "[awe] {driver} earns {achievement}." };
        saved.Events[nameof(AnnouncementEventType.LeaderChange)].Templates = new List<string> { "[shouting] Our own lead line, {driver}!" };
        VoiceSettingsStore.Load().Save(saved);

        var loaded = VoiceSettingsStore.Load();

        Assert.False(loaded.RefreshedLines, "not a refresh");
        Assert.Equal(VoiceSettingsStore.CurrentTemplateVersion, loaded.Current.TemplateVersion);
        Assert.Equal("eleven_v3", loaded.Current.ModelId);
        Assert.Equal(
            new[] { "Our own PB line for {driver}!", "[cheerful] {driver} again." },
            loaded.Current.Events[nameof(AnnouncementEventType.PersonalBestLap)].Templates);
        Assert.Equal(new[] { "[awe] {driver} earns {achievement}." }, loaded.Current.Events[nameof(AnnouncementEventType.AchievementUnlocked)].Templates);
        Assert.Equal(new[] { "[shouting] Our own lead line, {driver}!" }, loaded.Current.Events[nameof(AnnouncementEventType.LeaderChange)].Templates);
    }

    [Fact]
    public void Routine_calls_are_read_plain()
    {
        var defaults = VoiceProfile.CreateDefault();

        foreach (var type in new[]
                 {
                     AnnouncementEventType.LapCompleted, AnnouncementEventType.PositionReport,
                     AnnouncementEventType.RaceStaging, AnnouncementEventType.CountdownStarted,
                     AnnouncementEventType.GridCall, AnnouncementEventType.MinutesLeft,
                 })
        {
            Assert.Null(AudioTagTiers.For(type));
            Assert.All(defaults.Events[type.ToString()].EffectiveTemplates, line => Assert.DoesNotMatch(Loud, line));
        }
    }

    [Fact]
    public void A_pass_for_the_lead_shouts_and_a_pass_for_a_place_is_excited()
    {
        Assert.StartsWith("[shouting]", AnnouncementService.BattlePassForTheLead);
        Assert.StartsWith("[shouting]", AnnouncementService.RulingPassForTheLead);
        Assert.StartsWith("[shouting]", AnnouncementService.ReportCorrectionForTheLead);
        Assert.StartsWith("[shouting]", AnnouncementService.ReportRulingForTheLead);
        Assert.StartsWith("[excited]", AnnouncementService.BattlePassForAPlace);
        Assert.StartsWith("[excited]", AnnouncementService.RulingPassForAPlace);
        Assert.StartsWith("[excited]", AnnouncementService.ReportCorrectionForAPlace);
        Assert.StartsWith("[excited]", AnnouncementService.ReportRulingForAPlace);
        Assert.All(new RivalryLine().Templates, line => Assert.StartsWith("[excited]", line));
    }

    private static CommentaryService Desk(bool v4) => new(ProfileStore.Load(), new AppSettings
    {
        CommentaryEnabled = true,
        CommentaryToneStyle = CommentaryTone.Enthusiastic,
        CommentaryVoiceTags = true,
    })
    {
        VoiceSupportsAudioTagsProvider = () => true,
        VoiceCarriesTagsForwardProvider = () => v4,
    };

    [Fact]
    public void Commentary_on_v4_tags_only_its_first_sentence_and_v3_keeps_one_per_sentence()
    {
        const string recap = "Sam wins it. Bill was second. Frank took third.";

        Assert.Equal("[excited] Sam wins it. Bill was second. Frank took third.", Desk(v4: true).PrepareSummaryForSpeech(recap));
        Assert.Equal(
            "[excited] Sam wins it. [excited] Bill was second. [excited] Frank took third.",
            Desk(v4: false).PrepareSummaryForSpeech(recap));
        Assert.Equal(
            "[shouting] Welcome along. Big night ahead.",
            Desk(v4: true).PrepareSegmentForSpeech(SegmentKind.ColdOpen, "Welcome along. Big night ahead."));
    }

    [Fact]
    public void A_new_voice_starts_on_v4_turbo_which_carries_tags_forward()
    {
        var voice = VoiceProfile.CreateDefault();
        voice.Engine = VoiceEngine.ElevenLabs;

        Assert.Equal("eleven_v4_turbo", voice.ModelId);
        Assert.True(voice.SupportsAudioTags);
        Assert.True(voice.CarriesTagsForward);

        voice.ModelId = "eleven_v3";
        Assert.True(voice.SupportsAudioTags);
        Assert.False(voice.CarriesTagsForward);
    }

    [Fact]
    public void An_older_settings_file_moves_to_v4_turbo_with_the_new_lines_and_keeps_its_toggles()
    {
        var old = VoiceProfile.CreateDefault();
        old.TemplateVersion = 3;
        old.ModelId = "eleven_v3";
        old.RaceCallModelId = "eleven_flash_v2_5";
        old.Events[nameof(AnnouncementEventType.LapTime)].Templates = new List<string> { "[excited] {laptime}" };
        old.Events[nameof(AnnouncementEventType.LapTime)].Enabled = false;
        VoiceSettingsStore.Load().Save(old);

        var loaded = VoiceSettingsStore.Load();

        Assert.True(loaded.RefreshedLines);
        Assert.Equal(VoiceSettingsStore.CurrentTemplateVersion, loaded.Current.TemplateVersion);
        Assert.Equal("eleven_v4_turbo", loaded.Current.ModelId);
        Assert.Equal(string.Empty, loaded.Current.RaceCallModelId);
        Assert.Equal(new[] { "{laptime}" }, loaded.Current.Events[nameof(AnnouncementEventType.LapTime)].Templates);
        Assert.False(loaded.Current.Events[nameof(AnnouncementEventType.LapTime)].Enabled);

        Assert.False(VoiceSettingsStore.Load().RefreshedLines, "only once");
    }
}
