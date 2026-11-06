using System.Reflection;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// When the ElevenLabs allowance runs out (5 Nov 2026, asked for by Asher:
/// "when eleven labs allowance runs out each cast member should have their
/// own kokoro voice that is configurable underneath the eleven labs. when it
/// kicks over to kokoro no saved elevenlabs should play for continuity").
///
/// Each of the cast has a Kokoro voice of their own for the stand-in
/// (SecondVoice.StandInKokoroVoice, CastStandIns), and while ElevenLabs'
/// quota is used up no saved or prepared ElevenLabs take plays
/// (AnnouncementService.ElevenLabsSavedLinesPassedOver).
/// </summary>
public sealed class CastStandInTests : IDisposable
{
    private readonly TempAppData _data = new();

    // VoiceLibrary.Shared is one folder for the whole run - see BugHunt7ConsentTests.
    private readonly List<(VoiceProfile Profile, string Text)> _saved = new();

    public void Dispose()
    {
        foreach (var (profile, text) in _saved)
        {
            VoiceLibrary.Shared.Forget(profile, text);
        }

        _data.Dispose();
    }

    private static VoiceProfile ElevenLabsAnnouncer() => new()
    {
        Engine = VoiceEngine.ElevenLabs,
        ApiKey = "key",
        VoiceId = "announcer-id",
        VoiceName = "Adam",
        KokoroVoiceName = KokoroSpeech.DefaultVoice,
        KokoroStandsIn = true,
    };

    /// <summary>Every speaker on the Cast tab, as AnnouncementService is handed them.</summary>
    private static IReadOnlyList<(string Who, SecondVoice Voice)> Cast(AppSettings settings) => new[]
    {
        ("recap host", settings.RecapHostVoice),
        ("co-host", settings.CoHostVoice),
        ("Technical Lead", settings.MechanicVoice),
        ("Team Principal", settings.PrincipalVoice),
        ("achievements reader", settings.HonoursVoice),
    };

    /// <summary>A club whose cast each have an ElevenLabs voice of their own, and nothing else picked.</summary>
    private static AppSettings ElevenLabsCast() => new()
    {
        RecapHostElevenLabsVoiceId = "recap-id",
        CoHostElevenLabsVoiceId = "cohost-id",
        MechanicElevenLabsVoiceId = "mechanic-id",
        PrincipalElevenLabsVoiceId = "principal-id",
        HonoursElevenLabsVoiceId = "honours-id",
    };

    // --- The setting ------------------------------------------------------

    [Fact]
    public void Each_stand_in_voice_round_trips_through_the_settings_file()
    {
        var settings = new AppSettings
        {
            RecapHostKokoroStandInVoiceName = "bf_emma",
            CoHostKokoroStandInVoiceName = "am_onyx",
            MechanicKokoroStandInVoiceName = "bm_daniel",
            PrincipalKokoroStandInVoiceName = "af_sky",
            HonoursKokoroStandInVoiceName = "bf_lily",
        };

        settings.Save();
        var loaded = AppSettings.Load();

        Assert.Equal("bf_emma", loaded.RecapHostVoice.StandInKokoroVoice);
        Assert.Equal("am_onyx", loaded.CoHostVoice.StandInKokoroVoice);
        Assert.Equal("bm_daniel", loaded.MechanicVoice.StandInKokoroVoice);
        Assert.Equal("af_sky", loaded.PrincipalVoice.StandInKokoroVoice);
        Assert.Equal("bf_lily", loaded.HonoursVoice.StandInKokoroVoice);
    }

    [Fact]
    public void An_older_settings_file_gives_each_of_the_cast_a_different_kokoro_voice_none_of_them_the_announcers()
    {
        // A file from before the setting: none of the keys at all.
        var older = JsonSerializer.Deserialize<AppSettings>("{\"CoHostElevenLabsVoiceId\":\"cohost-id\"}")!;

        var voices = Cast(older).Select(c => c.Voice.StandInKokoroVoice).ToList();

        Assert.All(voices, v => Assert.False(string.IsNullOrWhiteSpace(v)));
        Assert.Equal(voices.Count, voices.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(KokoroSpeech.DefaultVoice, voices, StringComparer.OrdinalIgnoreCase);

        // And every default is a voice Kokoro has - so it is heard, not swapped for the announcer's.
        var known = new[] { CastStandIns.RecapHost, CastStandIns.CoHost, CastStandIns.TechnicalLead, CastStandIns.TeamPrincipal, CastStandIns.AchievementsReader };
        Assert.All(known, v => Assert.Contains(KokoroSpeech.InstalledVoices(), installed => installed.Name == v));
    }

    [Fact]
    public void An_older_file_with_a_kokoro_voice_picked_for_someone_stands_in_with_that_one()
    {
        var older = JsonSerializer.Deserialize<AppSettings>("{\"MechanicKokoroVoiceName\":\"bf_alice\"}")!;

        Assert.Equal("bf_alice", older.MechanicVoice.StandInKokoroVoice);

        // A stand-in pick of its own comes first.
        older.MechanicKokoroStandInVoiceName = "bm_fable";
        Assert.Equal("bm_fable", older.MechanicVoice.StandInKokoroVoice);
    }

    // --- Who speaks in which Kokoro voice --------------------------------------

    [Fact]
    public void Beside_an_elevenlabs_announcer_each_of_the_cast_carries_their_own_stand_in_voice()
    {
        var announcer = ElevenLabsAnnouncer();
        var settings = ElevenLabsCast();
        settings.CoHostKokoroStandInVoiceName = "af_nicole";

        var heard = new Dictionary<string, string>();

        foreach (var (who, voice) in Cast(settings))
        {
            var copy = announcer.CloneWithVoice(voice, kokoroReady: true);

            // Still ElevenLabs - the stand-in voice is only for when it can't speak.
            Assert.Equal(VoiceEngine.ElevenLabs, copy.EffectiveEngine);
            heard[who] = copy.KokoroVoiceName;
        }

        Assert.Equal("af_nicole", heard["co-host"]);
        Assert.Equal(CastStandIns.TechnicalLead, heard["Technical Lead"]);
        Assert.Equal(heard.Count, heard.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(announcer.KokoroVoiceName, heard.Values);

        // The announcer's own profile is untouched.
        Assert.Equal(KokoroSpeech.DefaultVoice, announcer.KokoroVoiceName);
    }

    [Fact]
    public void With_kokoro_chosen_the_cast_speak_in_their_kokoro_voices_not_their_stand_in_ones()
    {
        var announcer = new VoiceProfile { Engine = VoiceEngine.Kokoro, KokoroVoiceName = KokoroSpeech.DefaultVoice };
        var settings = new AppSettings
        {
            CoHostKokoroVoiceName = "bf_emma",
            CoHostKokoroStandInVoiceName = "am_onyx",
        };

        var copy = announcer.CloneWithVoice(settings.CoHostVoice, kokoroReady: true);

        Assert.Equal(VoiceEngine.Kokoro, copy.EffectiveEngine);
        Assert.Equal("bf_emma", copy.KokoroVoiceName);
    }

    [Fact]
    public void A_kokoro_voice_for_every_engine_is_still_the_one_they_speak_in()
    {
        var settings = new AppSettings
        {
            CoHostKokoroVoiceName = "bf_emma",
            CoHostKokoroAlways = true,
            CoHostKokoroStandInVoiceName = "am_onyx",
        };

        var copy = ElevenLabsAnnouncer().CloneWithVoice(settings.CoHostVoice, kokoroReady: true);

        Assert.Equal(VoiceEngine.Kokoro, copy.EffectiveEngine);
        Assert.Equal("bf_emma", copy.KokoroVoiceName);
    }

    // --- The Cast tab ----------------------------------------------------

    private static volatile string _now = "starting";

    [Fact]
    public void The_cast_tab_shows_each_stand_in_voice_and_saves_a_pick()
    {
        var problems = StaThread.Run(CheckStandInPickers, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static List<string> CheckStandInPickers()
    {
        var problems = new List<string>();
        var settings = ElevenLabsCast();

        _now = "building the Cast tab";

        using var cast = new CastControl(settings) { EngineInUse = () => VoiceEngine.ElevenLabs };

        static T Field<T>(CastControl cast, string name) => (T)typeof(CastControl)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cast)!;

        var pickers = new (string Name, Func<SecondVoice> Voice)[]
        {
            ("_cmbRecapHostStandIn", () => settings.RecapHostVoice),
            ("_cmbCoHostStandIn", () => settings.CoHostVoice),
            ("_cmbMechanicStandIn", () => settings.MechanicVoice),
            ("_cmbPrincipalStandIn", () => settings.PrincipalVoice),
            ("_cmbHonoursStandIn", () => settings.HonoursVoice),
        };

        foreach (var (name, voice) in pickers)
        {
            var picker = Field<ComboBox>(cast, name);

            if (picker.SelectedItem is not CastControl.VoiceChoice shown || shown.Id != voice().StandInKokoroVoice)
            {
                problems.Add($"{name} shows {picker.SelectedItem}, not {voice().StandInKokoroVoice}");
            }

            // Sits in the grid straight under the speaker's own voice row.
            if (picker.Parent?.Parent is not FieldGrid)
            {
                problems.Add($"{name} is not on a card's grid");
            }
        }

        _now = "picking a stand-in voice for the Technical Lead";

        var mechanic = Field<ComboBox>(cast, "_cmbMechanicStandIn");
        var other = mechanic.Items.OfType<CastControl.VoiceChoice>().First(c => c.Id != CastStandIns.TechnicalLead);
        mechanic.SelectedItem = other;

        if (settings.MechanicKokoroStandInVoiceName != other.Id || settings.MechanicVoice.StandInKokoroVoice != other.Id)
        {
            problems.Add($"picking {other.Id} saved \"{settings.MechanicKokoroStandInVoiceName}\"");
        }

        return problems;
    }

    // --- No saved ElevenLabs while Kokoro stands in --------------------------

    [Fact]
    public void Saved_elevenlabs_lines_are_passed_over_only_while_the_quota_is_used_up_and_kokoro_stands_in()
    {
        var elevenLabs = ElevenLabsAnnouncer();

        Assert.True(AnnouncementService.PassesOverSavedElevenLabs(elevenLabs, quotaUsedUp: true, kokoroReady: true));

        // The allowance back (a new month, a top-up): saved lines play as before.
        Assert.False(AnnouncementService.PassesOverSavedElevenLabs(elevenLabs, quotaUsedUp: false, kokoroReady: true));

        // Nothing to stand in: a saved line is better than silence.
        Assert.False(AnnouncementService.PassesOverSavedElevenLabs(elevenLabs, quotaUsedUp: true, kokoroReady: false));
        elevenLabs.KokoroStandsIn = false;
        Assert.False(AnnouncementService.PassesOverSavedElevenLabs(elevenLabs, quotaUsedUp: true, kokoroReady: true));

        // Not ElevenLabs' rule for the other voices.
        Assert.False(AnnouncementService.PassesOverSavedElevenLabs(
            new VoiceProfile { Engine = VoiceEngine.Kokoro }, quotaUsedUp: true, kokoroReady: true));
        Assert.False(AnnouncementService.PassesOverSavedElevenLabs(
            new VoiceProfile { Engine = VoiceEngine.HeatCaller, HeatCallerVoice = "clubhouse", KokoroStandsIn = true }, quotaUsedUp: true, kokoroReady: true));
    }

    [Fact]
    public async Task A_saved_elevenlabs_take_plays_until_the_quota_runs_out_then_kokoro_says_it_and_it_plays_again_once_the_quota_is_back()
    {
        const string text = "Drivers, this is the quota stand in check, bring your cars to the grid please";

        // No key: so nothing here goes to the network - with the saved take
        // passed over, the line goes straight to the stand-in.
        var voices = VoiceSettingsStore.Load();
        voices.Current.Engine = VoiceEngine.ElevenLabs;
        voices.Current.ApiKey = string.Empty;
        voices.Current.VoiceId = "quota-test-voice";
        voices.Current.KokoroStandsIn = true;

        var announcer = new AnnouncementService(ProfileStore.Load(), voices, SoundEffectsStore.Load());
        var profile = voices.Current.CloneForDelivery(0.4, 0.2);

        // Looks like an MP3 to the library, which keeps nothing else.
        var take = new byte[512];
        take[0] = (byte)'I';
        take[1] = (byte)'D';
        take[2] = (byte)'3';
        take[100] = 7;

        VoiceLibrary.Shared.Save(profile, text, take);
        _saved.Add((profile, text));

        for (var i = 0; i < 100 && !VoiceLibrary.Shared.Contains(profile, text); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(VoiceLibrary.Shared.Contains(profile, text));

        var (source, audio) = await RenderAsync(announcer, profile, text);
        Assert.Equal("Library", source);
        Assert.Equal(take, audio);

        if (!KokoroSpeech.IsAvailable)
        {
            // No Kokoro on this PC: nothing could stand in, so the saved take
            // rightly goes on playing - the rule itself is checked above.
            return;
        }

        // ElevenLabs refuses for quota: the saved take stands aside.
        typeof(AnnouncementService).GetField("_voiceQuotaExceeded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(announcer, true);

        (source, audio) = await RenderAsync(announcer, profile, text);
        Assert.Equal("StandIn", source);
        Assert.NotEqual(take, audio);

        // A prepared ElevenLabs take stops counting too.
        Assert.False(PreparedTakeMatches(announcer, "Library", profile));
        Assert.False(PreparedTakeMatches(announcer, "ElevenLabs", profile));

        // ElevenLabs answers again - a new month, a top-up - and the saved take is back.
        typeof(AnnouncementService).GetMethod("NoteElevenLabsAnswered", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(announcer, null);

        (source, audio) = await RenderAsync(announcer, profile, text);
        Assert.Equal("Library", source);
        Assert.Equal(take, audio);
        Assert.True(PreparedTakeMatches(announcer, "Library", profile));
    }

    /// <summary>AnnouncementService.RenderLineAsync, by reflection - it and what it returns are private.</summary>
    private static async Task<(string Source, byte[] Audio)> RenderAsync(AnnouncementService announcer, VoiceProfile profile, string text)
    {
        var task = (Task)typeof(AnnouncementService)
            .GetMethod("RenderLineAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(announcer, new object?[] { profile, text, false, CancellationToken.None })!;

        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var source = result.GetType().GetProperty("Source")!.GetValue(result)!.ToString()!;
        var audio = (byte[])result.GetType().GetProperty("Audio")!.GetValue(result)!;

        return (source, audio);
    }

    private static bool PreparedTakeMatches(AnnouncementService announcer, string source, VoiceProfile profile)
    {
        var method = typeof(AnnouncementService).GetMethod("PreparedTakeMatches", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lineSource = method.GetParameters()[1].ParameterType;

        return (bool)method.Invoke(announcer, new object?[] { "sig", Enum.Parse(lineSource, source), "sig", profile })!;
    }
}
