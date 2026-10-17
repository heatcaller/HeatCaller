using System.Net;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// The free Windows voice going, and Kokoro taking both its jobs (10 Oct
/// 2026).
///
/// The Windows voice was a choice a club could pick AND the voice that spoke
/// whenever anything else couldn't. Both are Kokoro's now, which leaves two
/// things that have to be right or a club loses its announcer:
///
///  - a club whose saved settings say "Windows" must come up on Kokoro, and
///    must not be quietly moved onto ElevenLabs, which it pays for by the
///    character;
///  - Kokoro's model may genuinely be absent (the installer ships it, but
///    publish.cmd only WARNS when it isn't there), and with nothing left
///    underneath it that means an announcer that cannot speak. It has to say
///    so, in words, naming the button that fixes it - never go quiet.
///
/// And the other half of the same change: hearing a HeatCaller voice before
/// committing to one. The free samples carry no token so they answer for a
/// club that has never signed up, and a preview of the club's own words
/// comes out of a separate daily allowance that must never fall back to the
/// race-night voice budget.
/// </summary>
public sealed class KokoroFallbackTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly HttpClient _realHttp = HeatCallerVoiceClient.Http;

    public void Dispose()
    {
        HeatCallerVoiceClient.Http = _realHttp;

        // Static, so it would follow this class into the next test.
        KokoroSpeech.RecordLoadFailureForTests(null, default);
        _data.Dispose();
    }

    // --- A saved Windows-voice setting --------------------------------------

    /// <summary>
    /// Writes voice-settings.json by hand, the way a club's own file looks.
    /// <paramref name="extra"/> goes in as written, so a test can put a key
    /// or a retired field in it.
    /// </summary>
    private void SaveSettingsFile(string extra) =>
        File.WriteAllText(
            _data.PathOf("voice-settings.json"),
            "{\n"
            + $"  \"TemplateVersion\": {VoiceSettingsStore.CurrentTemplateVersion},\n"
            + extra
            + "  \"VoiceName\": \"Club Voice\"\n"
            + "}\n");

    [Fact]
    public void A_club_whose_settings_chose_the_windows_voice_comes_up_on_kokoro()
    {
        // Engine 1 is the retired Windows voice - see VoiceEngines.
        SaveSettingsFile($"  \"Engine\": {VoiceEngines.RetiredWindows},\n");

        var store = VoiceSettingsStore.Load();

        Assert.Equal(VoiceEngine.Kokoro, store.Current.EffectiveEngine);

        // Written back, so the migration happens once rather than on every
        // launch - and an older version reading the file afterwards sees a
        // real choice rather than the number it wrote.
        Assert.Equal(VoiceEngine.Kokoro, VoiceSettingsStore.Load().Current.Engine);
    }

    [Fact]
    public void A_club_with_a_key_that_chose_the_windows_voice_is_not_moved_onto_elevenlabs()
    {
        // The dangerous one. This club pays ElevenLabs by the character and
        // had deliberately turned that voice off; inferring from the key
        // would have switched its announcer onto a bill it never asked for.
        // An older file's plain-text key, which the store takes in on load.
        SaveSettingsFile(
            $"  \"Engine\": {VoiceEngines.RetiredWindows},\n"
            + "  \"ApiKey\": \"club-key\",\n"
            + "  \"VoiceId\": \"club-voice\",\n");

        var store = VoiceSettingsStore.Load();

        Assert.Equal(VoiceEngine.Kokoro, store.Current.EffectiveEngine);

        // The key itself is kept - the club still has it, and choosing
        // ElevenLabs on the page must not mean typing it in again.
        Assert.Equal("club-key", store.Current.ApiKey);
    }

    [Fact]
    public void The_retired_windows_voice_name_is_ignored_and_leaves_the_file()
    {
        SaveSettingsFile(
            $"  \"Engine\": {VoiceEngines.RetiredWindows},\n"
            + "  \"WindowsVoiceName\": \"Microsoft Zira Desktop\",\n");

        // Read without throwing, which is the first thing that matters: a
        // settings file carrying a retired key must be ignored, not fatal.
        var store = VoiceSettingsStore.Load();

        Assert.Equal("Club Voice", store.Current.VoiceName);
        Assert.Equal(VoiceEngine.Kokoro, store.Current.EffectiveEngine);

        // And once it has been saved again, the dead field is gone rather
        // than sitting in the file looking like a setting.
        var written = File.ReadAllText(_data.PathOf("voice-settings.json"));
        Assert.DoesNotContain("WindowsVoiceName", written, StringComparison.Ordinal);

        // The stand-in's own name DOES stay, and that is not an oversight:
        // it is the same question with a different voice answering it, so
        // the JSON name was kept where the voice's name was dropped. See
        // VoiceProfile.KokoroStandsIn.
        Assert.Contains("WindowsVoiceStandsIn", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_club_that_turned_the_stand_in_off_still_has_it_off()
    {
        // The property is KokoroStandsIn now; the JSON name is the old one,
        // because the question is the same one with a different voice
        // answering it. Renaming it would have read as "never set" on every
        // existing file and turned the stand-in back on for exactly the
        // clubs that had chosen to do without it.
        SaveSettingsFile(
            "  \"Engine\": 0,\n"
            + "  \"WindowsVoiceStandsIn\": false,\n");

        Assert.False(VoiceSettingsStore.Load().Current.KokoroStandsIn);
    }

    [Fact]
    public void An_engine_number_this_version_does_not_know_is_worked_out_rather_than_kept()
    {
        // A file written by a newer version. Without a key there is nothing
        // to infer ElevenLabs from, so it lands on the free voice.
        SaveSettingsFile("  \"Engine\": 99,\n");

        Assert.Equal(VoiceEngine.Kokoro, VoiceSettingsStore.Load().Current.EffectiveEngine);
    }

    [Fact]
    public void A_new_install_speaks_with_kokoro()
    {
        // No ElevenLabs key, no relay, nothing set up: the announcer still
        // has a voice, and it is the free one.
        Assert.Equal(VoiceEngine.Kokoro, VoiceProfile.CreateDefault().EffectiveEngine);
        Assert.Equal(VoiceEngine.Kokoro, new VoiceProfile().EffectiveEngine);
    }

    // --- No Kokoro model on this PC -----------------------------------------

    /// <summary>
    /// As if Kokoro could not speak, however this PC is set up - a model
    /// that isn't there, or one that wouldn't load. Both, because the test
    /// machine may or may not have the bundled model beside the test dll.
    /// </summary>
    private static void NoKokoro() =>
        KokoroSpeech.RecordLoadFailureForTests("the Kokoro model wouldn't load", DateTime.UtcNow);

    /// <summary>A file the size of the model where a download puts it - empty, but all KokoroSpeech checks before loading.</summary>
    private static void KokoroIsThere()
    {
        KokoroSpeech.RecordLoadFailureForTests(null, default);
        Directory.CreateDirectory(KokoroSpeech.Folder);

        using var file = new FileStream(KokoroSpeech.DownloadedModelPath, FileMode.Create, FileAccess.Write);
        file.SetLength(60L * 1024 * 1024);
    }

    private static AnnouncementService AnnouncerOn(VoiceEngine engine, Action<VoiceProfile>? set = null)
    {
        var voices = VoiceSettingsStore.Load();
        voices.Current.Engine = engine;
        set?.Invoke(voices.Current);

        return new AnnouncementService(ProfileStore.Load(), voices, SoundEffectsStore.Load());
    }

    [Fact]
    public void With_no_kokoro_model_the_announcer_says_it_cannot_speak_at_all()
    {
        NoKokoro();

        var announcer = AnnouncerOn(VoiceEngine.Kokoro);

        // A blocker, not a stand-in. The difference is what the Mixer shows:
        // a blocker is the red NO VOICE banner, a stand-in is a mild notice
        // that something else is covering. Nothing covers for Kokoro now, so
        // claiming otherwise would be a lie a club only finds out about on
        // the night.
        Assert.NotNull(announcer.DescribeVoiceBlocker());
        Assert.Null(announcer.DescribeStandIn());
    }

    [Fact]
    public void The_console_line_for_no_voice_names_the_button_that_fixes_it()
    {
        var said = AnnouncementService.NoVoiceAtAllMessage("the Kokoro voice isn't on this PC");

        // Loud, and actionable. "Voice announcements unavailable" tells a
        // club nothing it can do at eight on a Saturday evening.
        Assert.Contains("NO VOICE", said, StringComparison.Ordinal);
        Assert.Contains("Voice tab", said, StringComparison.Ordinal);
        Assert.Contains("Download", said, StringComparison.Ordinal);
        Assert.Contains(KokoroSpeech.DownloadSizeText, said, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_kokoro_model_an_elevenlabs_club_with_no_key_is_told_so_too()
    {
        NoKokoro();

        var announcer = AnnouncerOn(VoiceEngine.ElevenLabs);

        // Nothing to ask ElevenLabs with and nothing to stand in: the one
        // state where a line really is lost, and it is reported as a blocker
        // rather than passed off as a stand-in.
        Assert.NotNull(announcer.DescribeVoiceBlocker());
        Assert.Null(announcer.DescribeStandIn());
    }

    [Fact]
    public void With_the_model_there_kokoro_stands_in_for_an_elevenlabs_club_with_no_key()
    {
        // The announcer first, then the model: building it is what decides
        // whether the model is loaded in the background, and a test must not
        // depend on 310 MB being read.
        var announcer = AnnouncerOn(VoiceEngine.ElevenLabs);

        KokoroIsThere();

        // Coalesced rather than asserted non-null first: an empty string
        // would not contain "Kokoro" either, and the suite is at zero
        // warnings - a possibly-null argument is one.
        var standIn = announcer.DescribeStandIn() ?? string.Empty;

        Assert.Contains("Kokoro", standIn, StringComparison.Ordinal);

        // A voice IS speaking, so this is not a blocker - the Mixer shows a
        // notice rather than the red banner.
        Assert.Null(announcer.DescribeVoiceBlocker());
    }

    [Fact]
    public void A_club_that_turned_the_stand_in_off_is_told_its_lines_are_skipped()
    {
        var announcer = AnnouncerOn(VoiceEngine.ElevenLabs, profile => profile.KokoroStandsIn = false);

        KokoroIsThere();

        // Their choice, and honoured - but with no key there is no voice at
        // all, and the page and the Mixer say which.
        Assert.Null(announcer.DescribeStandIn());
        Assert.NotNull(announcer.DescribeVoiceBlocker());
    }

    // --- Hear it before you buy it ------------------------------------------

    /// <summary>A relay on this laptop, which is an address the token may go to over plain http.</summary>
    private static AppSettings VoiceRelaySettings() => new()
    {
        VoiceRelayBaseUrl = "http://localhost:8787",
        VoiceRelayClub = "testclub",
        VoiceRelayToken = "club-token",
    };

    [Fact]
    public async Task The_free_samples_are_fetched_with_no_token_at_all()
    {
        // The whole point: this has to answer for a laptop that has never
        // been signed up, because that is the laptop deciding whether to.
        var relay = Serving(HttpStatusCode.OK, "application/json",
            "{\"samples\":[{\"id\":\"clubhouse\",\"name\":\"Clubhouse\",\"url\":\"/voice/sample/clubhouse\"},"
            + "{\"id\":\"trackside\",\"name\":\"Trackside\"}]}");

        var samples = await new HeatCallerVoiceClient(VoiceRelaySettings()).SamplesAsync();

        var asked = Assert.Single(relay.Asked);
        Assert.Equal("http://localhost:8787/voice/samples", asked.Url);
        Assert.Null(asked.Authorization);

        Assert.Equal(2, samples.Count);
        Assert.Equal("/voice/sample/clubhouse", samples[0].Url);

        // A voice the relay listed without a url still has one to fetch: the
        // shape the contract fixes, built from its id.
        Assert.Equal("/voice/sample/trackside", samples[1].Url);

        // What a picker shows. A record's own ToString would print its
        // fields, which is how "HeatCallerVoiceSample { Id = ..." ends up in
        // a drop-down.
        Assert.Equal("Trackside", samples[1].ToString());
    }

    [Fact]
    public async Task A_sample_address_pointing_somewhere_else_is_never_fetched()
    {
        var relay = Serving(HttpStatusCode.OK, "audio/mpeg", "not asked for");

        var client = new HeatCallerVoiceClient(VoiceRelaySettings());

        await Assert.ThrowsAsync<HeatCallerVoiceException>(
            () => client.SampleAsync("https://somewhere-else.example/voice/sample/clubhouse"));

        // Refused here, so nothing left this laptop. The token does not ride
        // on a sample request, but a relay answer that can send this app
        // anywhere is still not an answer to act on.
        Assert.Empty(relay.Asked);
    }

    [Fact]
    public async Task A_preview_sends_the_voice_and_the_words_with_the_clubs_token()
    {
        var relay = Serving(HttpStatusCode.OK, "audio/mpeg", "ID3 pretend mp3");

        var audio = await new HeatCallerVoiceClient(VoiceRelaySettings())
            .PreviewAsync("clubhouse", "Green flag, green flag!");

        Assert.NotEmpty(audio);

        var asked = Assert.Single(relay.Asked);
        Assert.Equal("http://localhost:8787/voice/preview", asked.Url);
        Assert.Equal("Bearer club-token", asked.Authorization);

        // voiceId and text, and nothing else: the model and the voice
        // settings are the relay's, exactly as they are for a race line, so
        // what a club hears here is what it will get.
        Assert.Contains("\"voiceId\":\"clubhouse\"", asked.Body, StringComparison.Ordinal);
        Assert.Contains("Green flag", asked.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_spent_preview_allowance_is_said_in_words_and_never_spends_the_nights_voice()
    {
        // 429 with a readable reason, as the relay's contract fixes it.
        var relay = Serving(HttpStatusCode.TooManyRequests, "application/json",
            "{\"error\":\"Today's previews are used up. They start again tomorrow.\",\"refusal\":\"busy\"}");

        var client = new HeatCallerVoiceClient(VoiceRelaySettings());

        var refused = await Assert.ThrowsAsync<HeatCallerVoiceException>(
            () => client.PreviewAsync("clubhouse", "Green flag, green flag!"));

        // The relay's own words reach the club rather than a status code.
        Assert.Contains("previews are used up", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(HeatCallerVoiceClient.IsPreviewAllowanceSpent(refused));

        // And nothing else was asked. This is the rule the whole separate
        // allowance exists for: a page where "try it again" quietly spends
        // the night's characters is a page a club cannot afford to explore,
        // so the preview must never fall through to /voice/speak.
        var asked = Assert.Single(relay.Asked);
        Assert.DoesNotContain("/voice/speak", asked.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_preview_line_longer_than_the_cap_is_refused_before_a_request_is_made()
    {
        var relay = Serving(HttpStatusCode.OK, "audio/mpeg", "ID3 pretend mp3");

        var client = new HeatCallerVoiceClient(VoiceRelaySettings());
        var tooLong = new string('a', HeatCallerVoiceClient.MaxPreviewCharacters + 1);

        var refused = await Assert.ThrowsAsync<HeatCallerVoiceException>(
            () => client.PreviewAsync("clubhouse", tooLong));

        Assert.Equal(HeatCallerRefusal.Text, refused.Refusal);
        Assert.Empty(relay.Asked);
    }

    [Fact]
    public async Task With_no_relay_address_the_samples_say_so_without_sending_anybody_to_set_up_publishing()
    {
        var relay = Serving(HttpStatusCode.OK, "application/json", "{\"samples\":[]}");

        var client = new HeatCallerVoiceClient(new AppSettings());

        Assert.False(client.SamplesReachable);

        var refused = await Assert.ThrowsAsync<HeatCallerVoiceException>(() => client.SamplesAsync());

        // Its own sentence, not the publishing one: a club that only wants
        // to HEAR the voices has not got as far as a token and should not be
        // told it must have one.
        Assert.Contains("samples", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(relay.Asked);
    }

    // --- A relay that answers without a relay -------------------------------

    /// <summary>One request this test saw, as much of it as a test has an opinion about.</summary>
    private sealed record Request(string Url, string? Authorization, string Body);

    /// <summary>Answers every request the same way, and remembers what was asked.</summary>
    private sealed class FakeRelay(HttpStatusCode status, string contentType, string body) : HttpMessageHandler
    {
        public List<Request> Asked { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // "sent", not "body". This local used to be called body, which
            // shadowed the constructor's body parameter - the answer this
            // relay was built to serve - so the response handed back
            // whatever had just been POSTED to it instead. An echo passes a
            // test that only looks at what was asked and quietly lies to any
            // test that reads the answer. The compiler said so (CS9113,
            // "parameter 'body' is unread") and that warning was the bug.
            var sent = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Asked.Add(new Request(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                sent));

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
                RequestMessage = request,
            };
        }
    }

    /// <summary>Points the voice client at a relay that answers this, and hands back what it was asked. Put back by Dispose.</summary>
    private static FakeRelay Serving(HttpStatusCode status, string contentType, string body)
    {
        var relay = new FakeRelay(status, contentType, body);
        HeatCallerVoiceClient.Http = new HttpClient(relay);
        return relay;
    }
}
