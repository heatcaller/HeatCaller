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

        KokoroSpeech.ModelForTests = null;
        KokoroSpeech.RecordLoadFailureForTests(null, default);
        _data.Dispose();
    }

    /// <summary>
    /// A Kokoro "model" on this PC that answers each line with the name of
    /// the voice that said it - the 310 MB model can't be loaded in a test
    /// (see BugHunt4VoiceTests), and the name is what these tests are about.
    /// </summary>
    /// <remarks>
    /// Only once the announcer is built: an AnnouncementService warms the
    /// real Kokoro up as it starts whenever a model is on the PC, and that
    /// warm-up loading this empty file records "the model wouldn't load"
    /// for a minute - Kokoro unable to speak for whatever test comes next.
    /// </remarks>
    /// <remarks>
    /// The stand-in model first, then the placeholder, then the slate
    /// cleared (open items, 19 Nov 2026). It cleared first and set the model
    /// last, and a warm-up left running by an earlier test that failed in
    /// between - while the 60 MB placeholder was written, which under a
    /// loaded full suite takes a while - was recorded as "the model wouldn't
    /// load" (KokoroSpeech only ignores one while a stand-in model is set),
    /// so Kokoro read as unavailable and the quota test heard the saved take.
    /// Now a failure before the model is set is cleared, and one after it
    /// is never recorded.
    /// </remarks>
    private static void FakeKokoro()
    {
        KokoroSpeech.ModelForTests = (name => name, (_, voice, _) => Task.FromResult(SaidBy((string)voice)));
        Directory.CreateDirectory(KokoroSpeech.Folder);

        using (var file = new FileStream(KokoroSpeech.ModelPath, FileMode.Create, FileAccess.Write))
        {
            file.SetLength(60L * 1024 * 1024);
        }

        KokoroSpeech.RecordLoadFailureForTests(null, default);
        Assert.True(KokoroSpeech.IsAvailable, KokoroSpeech.UnavailableReason);
    }

    /// <summary>A tenth of a second of "audio" - the least Kokoro takes for a line - starting with who said it.</summary>
    private static byte[] SaidBy(string voice)
    {
        var pcm = new byte[9600];
        Marker(voice).CopyTo(pcm, 0);
        return pcm;
    }

    private static byte[] Marker(string voice) => System.Text.Encoding.UTF8.GetBytes("kokoro:" + voice + "|");

    /// <summary>Whether this rendered line was said by that Kokoro voice - its PCM is inside the WAV Kokoro wraps it in.</summary>
    private static bool SpokenBy(byte[] audio, string voice) => audio.AsSpan().IndexOf(Marker(voice)) >= 0;

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

    /// <summary>
    /// Bug hunt 16 (10 Nov 2026): with no stand-in picked, a speaker stands
    /// in with their Kokoro voice (CastStandIns.Resolve), so a Kokoro pick on
    /// their Voice list changes who stands in. The stand-in row used to keep
    /// showing the voice it showed at start-up - and its Test Voice said
    /// that one, not the voice the show would use once ElevenLabs ran out.
    /// </summary>
    [Fact]
    public void A_kokoro_pick_above_a_stand_in_row_is_shown_on_it()
    {
        FakeKokoro();

        var problems = StaThread.Run(CheckStandInFollowsVoicePick, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static List<string> CheckStandInFollowsVoicePick()
    {
        var problems = new List<string>();
        var settings = ElevenLabsCast();

        _now = "building the Cast tab";

        using var cast = new CastControl(settings) { EngineInUse = () => VoiceEngine.ElevenLabs };

        static T Field<T>(CastControl cast, string name) => (T)typeof(CastControl)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cast)!;

        typeof(CastControl)
            .GetMethod("FillVoices", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(cast, new object?[] { VoiceEngine.ElevenLabs, null });

        var voice = Field<ComboBox>(cast, "_cmbCoHostVoice");
        var standIn = Field<ComboBox>(cast, "_cmbCoHostStandIn");

        string? Shown() => (standIn.SelectedItem as CastControl.VoiceChoice)?.Id;

        if (Shown() != CastStandIns.CoHost)
        {
            problems.Add($"the co-host's stand-in starts on {Shown()}, not {CastStandIns.CoHost}");
        }

        _now = "picking a Kokoro voice for the co-host, then an ElevenLabs one";

        var kokoro = voice.Items.OfType<CastControl.VoiceChoice>()
            .FirstOrDefault(c => c.IsKokoro && !string.Equals(c.Id, CastStandIns.CoHost, StringComparison.OrdinalIgnoreCase));
        var elevenLabs = voice.Items.OfType<CastControl.VoiceChoice>().FirstOrDefault(c => !c.IsKokoro && c.Id.Length > 0);

        if (kokoro is null || elevenLabs is null)
        {
            problems.Add($"the co-host's list has no Kokoro voice ({kokoro}) or no ElevenLabs voice ({elevenLabs})");
            return problems;
        }

        voice.SelectedItem = kokoro;
        voice.SelectedItem = elevenLabs;

        if (settings.CoHostVoice.StandInKokoroVoice != kokoro.Id)
        {
            problems.Add($"the co-host stands in with {settings.CoHostVoice.StandInKokoroVoice}, not {kokoro.Id}");
        }

        if (Shown() != settings.CoHostVoice.StandInKokoroVoice)
        {
            problems.Add($"the stand-in row shows {Shown()}, but the co-host stands in with {settings.CoHostVoice.StandInKokoroVoice}");
        }

        // Nothing was picked on the stand-in row itself.
        if (settings.CoHostKokoroStandInVoiceName.Length > 0)
        {
            problems.Add($"showing the stand-in again saved \"{settings.CoHostKokoroStandInVoiceName}\" as a pick");
        }

        _now = "changing the co-host's Kokoro voice elsewhere and showing the tab again";

        // As a pick from Kokoro's own list does, with Kokoro the engine then.
        var other = standIn.Items.OfType<CastControl.VoiceChoice>().First(c => c.Id != kokoro.Id && c.Id != CastStandIns.CoHost);
        settings.CoHostKokoroVoiceName = other.Id;

        typeof(Control)
            .GetMethod("OnVisibleChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(cast, new object?[] { EventArgs.Empty });

        if (Shown() != other.Id)
        {
            problems.Add($"after the tab is shown again the stand-in row shows {Shown()}, not {other.Id}");
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

    /// <summary>
    /// An ElevenLabs announcer with a take of <paramref name="text"/> in the
    /// saved-voice library, and the usage chip's ElevenLabs meter as
    /// <paramref name="meterSpent"/> says.
    /// </summary>
    private async Task<(AnnouncementService Announcer, VoiceProfile Profile, byte[] Take)> AnnouncerWithSavedTakeAsync(
        string text, string apiKey, Func<bool> meterSpent)
    {

        var voices = VoiceSettingsStore.Load();
        voices.Current.Engine = VoiceEngine.ElevenLabs;
        voices.Current.ApiKey = apiKey;
        voices.Current.VoiceId = "quota-test-voice";
        voices.Current.KokoroStandsIn = true;

        var announcer = new AnnouncementService(ProfileStore.Load(), voices, SoundEffectsStore.Load())
        {
            ElevenLabsMeterSpent = meterSpent,
        };

        // After the announcer - see FakeKokoro.
        FakeKokoro();

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
        return (announcer, profile, take);
    }

    /// <summary>
    /// THE FLAKE (15 Nov 2026). The test below failed once in a full run -
    /// "Library" where "StandIn" was due - and passed alone. An announcer
    /// warms the real Kokoro up on a background task (KokoroSpeech.WarmUp),
    /// and a warm-up left over from an earlier test can run late: it reads
    /// ModelPath when it runs, finds THIS test's 60 MB placeholder, fails to
    /// load it, and records "the model wouldn't load" - so Kokoro reads as
    /// unavailable for a minute, the saved ElevenLabs take is not passed
    /// over, and the stand-in never speaks. A real load failing behind a
    /// test's stand-in model says nothing about that model, so it is no
    /// longer recorded while one is set.
    /// </summary>
    [Fact]
    public async Task A_stray_kokoro_warm_up_behind_the_test_model_leaves_kokoro_available()
    {
        FakeKokoro();
        Assert.True(KokoroSpeech.IsAvailable);

        // What the leftover warm-up does: try the real engine on the placeholder.
        KokoroSpeech.WarmUp();

        var loading = typeof(KokoroSpeech).GetField("_loading", BindingFlags.Static | BindingFlags.NonPublic)!;

        for (var i = 0; i < 200 && loading.GetValue(null) is not Task { IsCompleted: true }; i++)
        {
            await Task.Delay(50);
        }

        Assert.True(loading.GetValue(null) is Task { IsCompleted: true }, "the warm-up never finished");
        Assert.True(KokoroSpeech.IsAvailable, KokoroSpeech.UnavailableReason);
    }

    [Fact]
    public async Task At_100_percent_on_the_usage_chip_kokoro_says_a_saved_line_and_at_under_100_the_saved_take_plays_again()
    {
        const string text = "Drivers, this is the usage chip stand in check, bring your cars to the grid please";

        var spent = false;
        var (announcer, profile, take) = await AnnouncerWithSavedTakeAsync(text, "not-a-real-key", () => spent);

        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        announcer.StatusMessage += log.Enqueue;

        var (source, audio) = await RenderAsync(announcer, profile, text);
        Assert.Equal("Library", source);
        Assert.Equal(take, audio);

        // The chip reaches 100%.
        spent = true;

        (source, audio) = await RenderAsync(announcer, profile, text);
        Assert.Equal("StandIn", source);
        Assert.True(SpokenBy(audio, KokoroSpeech.DefaultVoice), $"not said by {KokoroSpeech.DefaultVoice}");
        Assert.Contains(log, line => line.Contains("100% on the usage chip", StringComparison.Ordinal));

        // And each of the cast in their own Kokoro voice - here the Technical
        // Lead's default, from a settings file that never picked one.
        var mechanic = profile.CloneWithVoice(ElevenLabsCast().MechanicVoice, kokoroReady: true);
        (source, audio) = await RenderAsync(announcer, mechanic, "Thanks, the car was mega tonight");
        Assert.Equal("StandIn", source);
        Assert.True(SpokenBy(audio, CastStandIns.TechnicalLead), $"not said by {CastStandIns.TechnicalLead}");

        // ElevenLabs was never asked - a key this fake would have been turned down.
        Assert.DoesNotContain(log, line => line.Contains("turned the API key down", StringComparison.Ordinal)
            || line.Contains("Can't reach ElevenLabs", StringComparison.Ordinal));

        // A prepared ElevenLabs take stops counting too, and the Mixer says why Kokoro speaks.
        Assert.False(PreparedTakeMatches(announcer, "Library", profile));
        Assert.False(PreparedTakeMatches(announcer, "ElevenLabs", profile));
        Assert.Contains("100%", announcer.DescribeStandIn());

        // A new month (or a bigger plan): under 100%, and the saved take is back.
        spent = false;

        (source, audio) = await RenderAsync(announcer, profile, text);
        Assert.Equal("Library", source);
        Assert.Equal(take, audio);
        Assert.True(PreparedTakeMatches(announcer, "Library", profile));
        Assert.Contains(log, line => line.Contains("under 100% again", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_quota_refusal_with_the_chip_behind_also_holds_the_saved_take_back_until_elevenlabs_answers()
    {
        const string text = "Drivers, this is the quota refusal stand in check, bring your cars to the grid please";

        // No key: so nothing here goes to the network - with the saved take
        // passed over, the line goes straight to the stand-in.
        var (announcer, profile, take) = await AnnouncerWithSavedTakeAsync(text, string.Empty, () => false);

        typeof(AnnouncementService).GetField("_voiceQuotaExceeded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(announcer, true);

        var (source, audio) = await RenderAsync(announcer, profile, text);
        Assert.Equal("StandIn", source);
        Assert.True(SpokenBy(audio, KokoroSpeech.DefaultVoice), $"not said by {KokoroSpeech.DefaultVoice}");

        typeof(AnnouncementService).GetMethod("NoteElevenLabsAnswered", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(announcer, null);

        (source, audio) = await RenderAsync(announcer, profile, text);
        Assert.Equal("Library", source);
        Assert.Equal(take, audio);
    }

    [Theory]
    [InlineData(9_999, 10_000, false)]
    [InlineData(10_000, 10_000, true)]
    [InlineData(10_450, 10_000, true)]
    public void The_trigger_is_exactly_when_the_chip_says_100_percent(long used, long allowance, bool spent)
    {
        var meter = new Meter(MeterKind.ElevenLabsKey, true, used, allowance, 0, null, DateTime.UtcNow, 0, null);

        Assert.Equal(spent, meter.Spent);
        Assert.Equal(spent, meter.ShortPercent() is var shown && int.Parse(shown.TrimEnd('%')) >= 100);
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
