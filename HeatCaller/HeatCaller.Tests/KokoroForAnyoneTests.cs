namespace HeatCaller.Tests;

/// <summary>
/// A Kokoro voice for any of the cast whatever engine the announcer uses
/// (25 Oct 2026, asked for by Asher: "is the kokoro voice selectable for all
/// voices? if not make it so").
///
/// Before this, each speaker's voice was picked per engine and the Cast
/// tab's lists held only the engine in use - so beside an ElevenLabs or
/// HeatCaller announcer, nobody could be given a Kokoro voice. Now a Kokoro
/// voice picked from another engine's list is kept with KokoroAlways, and
/// VoiceProfile.CloneWithVoice sends that speaker's lines to Kokoro.
/// </summary>
public sealed class KokoroForAnyoneTests
{
    private static VoiceProfile ElevenLabsAnnouncer() => new()
    {
        Engine = VoiceEngine.ElevenLabs,
        ApiKey = "key",
        VoiceId = "announcer-id",
        VoiceName = "Adam",
        KokoroVoiceName = "am_michael",
        Stability = 0.3,
        Style = 0.4,
    };

    private static SecondVoice KokoroForEveryEngine(string name) =>
        new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, name, KokoroAlways: true);

    [Fact]
    public void A_kokoro_voice_for_every_engine_speaks_in_kokoro_beside_an_elevenlabs_announcer()
    {
        var announcer = ElevenLabsAnnouncer();

        var copy = announcer.CloneWithVoice(KokoroForEveryEngine("bf_emma"), kokoroReady: true);

        Assert.Equal(VoiceEngine.Kokoro, copy.EffectiveEngine);
        Assert.Equal("bf_emma", copy.KokoroVoiceName);

        // The delivery and speed are the announcer's, as for any second voice.
        Assert.Equal(0.3, copy.Stability);
        Assert.Equal(0.4, copy.Style);

        // And the saved profile is untouched - the announcer still speaks ElevenLabs.
        Assert.Equal(VoiceEngine.ElevenLabs, announcer.EffectiveEngine);
        Assert.Equal("am_michael", announcer.KokoroVoiceName);
    }

    [Fact]
    public void And_beside_a_heatcaller_announcer()
    {
        var announcer = new VoiceProfile
        {
            Engine = VoiceEngine.HeatCaller,
            HeatCallerVoice = "clubhouse",
            HeatCallerVoiceName = "Clubhouse",
        };

        var copy = announcer.CloneWithVoice(KokoroForEveryEngine("am_onyx"), kokoroReady: true);

        Assert.Equal(VoiceEngine.Kokoro, copy.EffectiveEngine);
        Assert.Equal("am_onyx", copy.KokoroVoiceName);
        Assert.Equal(VoiceEngine.HeatCaller, announcer.EffectiveEngine);
    }

    [Fact]
    public void Without_kokoro_on_the_pc_the_line_goes_out_as_it_did_before_rather_than_not_at_all()
    {
        var copy = ElevenLabsAnnouncer().CloneWithVoice(KokoroForEveryEngine("bf_emma"), kokoroReady: false);

        Assert.Equal(VoiceEngine.ElevenLabs, copy.EffectiveEngine);
        Assert.Equal("announcer-id", copy.VoiceId);
    }

    [Fact]
    public void A_kokoro_voice_picked_only_for_kokoro_does_not_change_the_engine()
    {
        // The rule as it always was: a Kokoro name with KokoroAlways off is
        // the voice for when Kokoro is the engine (or stands in), nothing more.
        var perEngine = new SecondVoice("mechanic-id", "Bill", string.Empty, string.Empty, string.Empty, "bf_emma");

        var copy = ElevenLabsAnnouncer().CloneWithVoice(perEngine, kokoroReady: true);

        Assert.Equal(VoiceEngine.ElevenLabs, copy.EffectiveEngine);
        Assert.Equal("mechanic-id", copy.VoiceId);
        Assert.False(perEngine.SpeaksKokoroWith(VoiceEngine.ElevenLabs));
    }

    [Fact]
    public void A_kokoro_voice_for_every_engine_counts_as_a_voice_picked_for_each()
    {
        var voice = KokoroForEveryEngine("bf_emma");

        Assert.True(voice.IsSetFor(VoiceEngine.ElevenLabs));
        Assert.True(voice.IsSetFor(VoiceEngine.HeatCaller));
        Assert.True(voice.IsSetFor(VoiceEngine.Kokoro));

        Assert.True(voice.SpeaksKokoroWith(VoiceEngine.ElevenLabs));
        Assert.False(voice.SpeaksKokoroWith(VoiceEngine.Kokoro));

        Assert.Equal("Emma (British, female) (Kokoro)", voice.NameFor(VoiceEngine.ElevenLabs));
        Assert.Equal("Emma (British, female)", voice.NameFor(VoiceEngine.Kokoro));
    }

    [Fact]
    public void Kokoro_always_with_no_voice_named_is_no_voice()
    {
        var voice = KokoroForEveryEngine(string.Empty);

        Assert.False(voice.SpeaksKokoroWith(VoiceEngine.ElevenLabs));
        Assert.False(voice.IsSetFor(VoiceEngine.ElevenLabs));
        Assert.Equal(VoiceEngine.ElevenLabs, ElevenLabsAnnouncer().CloneWithVoice(voice, kokoroReady: true).EffectiveEngine);
    }

    [Fact]
    public void Every_speaker_on_the_cast_carries_the_setting_into_their_voice()
    {
        var settings = new AppSettings
        {
            CoHostKokoroVoiceName = "af_bella", CoHostKokoroAlways = true,
            MechanicKokoroVoiceName = "am_adam", MechanicKokoroAlways = true,
            PrincipalKokoroVoiceName = "bm_george", PrincipalKokoroAlways = true,
            HonoursKokoroVoiceName = "bf_lily", HonoursKokoroAlways = true,
            RecapHostKokoroVoiceName = "am_puck", RecapHostKokoroAlways = true,
        };

        foreach (var (voice, name) in new[]
                 {
                     (settings.CoHostVoice, "af_bella"),
                     (settings.MechanicVoice, "am_adam"),
                     (settings.PrincipalVoice, "bm_george"),
                     (settings.HonoursVoice, "bf_lily"),
                     (settings.RecapHostVoice, "am_puck"),
                 })
        {
            Assert.True(voice.KokoroAlways);
            Assert.Equal(name, voice.KokoroVoiceName);
            Assert.True(voice.SpeaksKokoroWith(VoiceEngine.ElevenLabs));
        }

        // Off by default: a club that never picks one hears what it always did.
        var fresh = new AppSettings();
        Assert.False(fresh.CoHostVoice.KokoroAlways);
        Assert.False(fresh.MechanicVoice.KokoroAlways);
        Assert.False(fresh.PrincipalVoice.KokoroAlways);
        Assert.False(fresh.HonoursVoice.KokoroAlways);
        Assert.False(fresh.RecapHostVoice.KokoroAlways);
    }

    [Fact]
    public void The_setting_survives_a_save_and_a_load()
    {
        var settings = new AppSettings { CoHostKokoroVoiceName = "af_bella", CoHostKokoroAlways = true };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.True(back.CoHostKokoroAlways);
        Assert.Equal("af_bella", back.CoHostKokoroVoiceName);
    }

    // --- The Cast tab's lists -------------------------------------------------------------

    private static volatile string _now = "starting";

    [Fact]
    public void Beside_an_elevenlabs_announcer_the_cast_lists_offer_the_kokoro_voices_and_a_pick_is_kept_for_every_engine()
    {
        var problems = StaThread.Run(CheckCastLists, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static List<string> CheckCastLists()
    {
        var problems = new List<string>();
        var settings = new AppSettings();

        _now = "building the Cast tab";

        using var cast = new CastControl(settings) { EngineInUse = () => VoiceEngine.ElevenLabs };

        // As it is set out when shown - the host sets EngineInUse after building it.
        typeof(CastControl)
            .GetMethod("FillVoices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(cast, new object?[] { VoiceEngine.ElevenLabs, null });

        var picker = (ComboBox)typeof(CastControl)
            .GetField("_cmbCoHostVoice", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(cast)!;

        var kokoro = picker.Items.OfType<CastControl.VoiceChoice>().Where(c => c.IsKokoro).ToList();

        if (!KokoroSpeech.IsModelAvailable)
        {
            // Not offered on a PC without the model: a voice that would never be heard.
            if (kokoro.Count > 0)
            {
                problems.Add($"{kokoro.Count} Kokoro voices offered with no Kokoro model on this PC");
            }

            return problems;
        }

        _now = "picking a Kokoro voice for the co-host";

        if (kokoro.Count == 0)
        {
            problems.Add("no Kokoro voices on the co-host's list beside an ElevenLabs announcer");
            return problems;
        }

        if (!kokoro.All(c => c.Display.StartsWith("Kokoro: ", StringComparison.Ordinal)))
        {
            problems.Add("a Kokoro voice on the ElevenLabs list is not marked as Kokoro's");
        }

        picker.SelectedItem = kokoro[0];

        if (!settings.CoHostKokoroAlways || settings.CoHostKokoroVoiceName != kokoro[0].Id)
        {
            problems.Add($"picking {kokoro[0].Id} saved KokoroAlways={settings.CoHostKokoroAlways}, name \"{settings.CoHostKokoroVoiceName}\"");
        }

        // Set out again, it is still the one shown.
        typeof(CastControl)
            .GetMethod("FillVoices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(cast, new object?[] { VoiceEngine.ElevenLabs, null });

        if (picker.SelectedItem is not CastControl.VoiceChoice { IsKokoro: true } shown || shown.Id != kokoro[0].Id)
        {
            problems.Add($"after filling the lists again the co-host's list shows {picker.SelectedItem}");
        }

        // The announcer's own voice ends it.
        _now = "picking the announcer's own voice";
        picker.SelectedIndex = 0;

        if (settings.CoHostKokoroAlways)
        {
            problems.Add("picking the announcer's own voice left the co-host on Kokoro");
        }

        return problems;
    }
}
