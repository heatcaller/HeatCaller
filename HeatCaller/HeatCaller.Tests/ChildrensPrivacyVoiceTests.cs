using System.Net;

namespace HeatCaller.Tests;

/// <summary>
/// Children's privacy, plan 2.3 (15 Nov 2026): ElevenLabs is asked not to keep
/// the club's lines, and - with "Offline voices for juniors" on - a line that
/// names a junior is said by Kokoro and commentary about a junior is written
/// by the model on this PC, never sent to a cloud voice or the relay.
/// </summary>
[Collection(ElevenLabsHttpCollection.Name)]
public sealed class ChildrensPrivacyVoiceTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly HttpClient _realHttp = ElevenLabsClient.Http;
    private readonly Func<bool> _realAsk = ElevenLabsClient.AskNotToKeepLines;
    private readonly Action<string>? _realNotice = ElevenLabsClient.Notice;

    public ChildrensPrivacyVoiceTests()
    {
        ElevenLabsClient.AskNotToKeepLines = () => true;
        ElevenLabsClient.ForgetNoLoggingRefusals();
    }

    public void Dispose()
    {
        ElevenLabsClient.Http = _realHttp;
        ElevenLabsClient.AskNotToKeepLines = _realAsk;
        ElevenLabsClient.Notice = _realNotice;
        ElevenLabsClient.ForgetNoLoggingRefusals();
        _data.Dispose();
    }

    // ------------------------------------------------------------------
    // ElevenLabs: don't keep our lines
    // ------------------------------------------------------------------

    private static VoiceProfile Profile(string key = "key-1", string format = "") => new()
    {
        ApiKey = key,
        VoiceId = "voice/1",
        OutputFormat = format,
        Engine = VoiceEngine.ElevenLabs,
    };

    [Fact]
    public void The_setting_is_on_by_default()
    {
        Assert.True(new AppSettings().ElevenLabsNoLogging);
    }

    [Fact]
    public void The_speech_url_carries_enable_logging_false_after_any_query()
    {
        Assert.Equal("v1/text-to-speech/voice%2F1?enable_logging=false", ElevenLabsClient.SpeechUrl(Profile(), stream: false, noLogging: true));
        Assert.Equal("v1/text-to-speech/voice%2F1?output_format=mp3_44100_128&enable_logging=false",
            ElevenLabsClient.SpeechUrl(Profile(format: "mp3_44100_128"), stream: false, noLogging: true));
        Assert.Equal($"v1/text-to-speech/voice%2F1/stream?output_format={StreamingVoiceLine.ElevenLabsFormat}&enable_logging=false",
            ElevenLabsClient.SpeechUrl(Profile(), stream: true, noLogging: true));
        Assert.Equal("v1/text-to-speech/voice%2F1/with-timestamps?enable_logging=false",
            ElevenLabsClient.SpeechUrl(Profile(), stream: false, noLogging: true, endpoint: "/with-timestamps"));
        Assert.Equal("v1/text-to-speech/voice%2F1", ElevenLabsClient.SpeechUrl(Profile(), stream: false, noLogging: false));
    }

    [Fact]
    public async Task A_line_is_sent_with_the_flag_and_without_it_when_the_box_is_unticked()
    {
        var server = Serve(HttpStatusCode.OK, HttpStatusCode.OK);

        await new ElevenLabsClient().SynthesizeAsync(Profile("key-on"), "Green flag!");
        ElevenLabsClient.AskNotToKeepLines = () => false;
        await new ElevenLabsClient().SynthesizeAsync(Profile("key-on"), "Green flag!");

        Assert.Equal(2, server.Urls.Count);
        Assert.EndsWith("enable_logging=false", server.Urls[0]);
        Assert.DoesNotContain("enable_logging", server.Urls[1]);
    }

    [Fact]
    public async Task A_plan_that_turns_the_flag_down_still_gets_its_line_and_is_remembered()
    {
        var said = new List<string>();
        ElevenLabsClient.Notice = said.Add;
        var server = Serve(HttpStatusCode.Forbidden, HttpStatusCode.OK, HttpStatusCode.OK);

        var audio = await new ElevenLabsClient().SynthesizeAsync(Profile("key-refused"), "Green flag!");
        await new ElevenLabsClient().SynthesizeAsync(Profile("key-refused"), "Yellow flag!");

        Assert.Equal(new byte[] { 1, 2, 3 }, audio);
        Assert.Equal(3, server.Urls.Count);
        Assert.Contains("enable_logging=false", server.Urls[0]);
        Assert.DoesNotContain("enable_logging", server.Urls[1]);
        Assert.DoesNotContain("enable_logging", server.Urls[2]);
        Assert.Single(said);
        Assert.Contains("403", said[0]);

        // Per key: another club key on this PC is still asked.
        Assert.True(ElevenLabsClient.AsksNotToLog("key-other"));
        Assert.False(ElevenLabsClient.AsksNotToLog("key-refused"));
    }

    [Fact]
    public async Task A_request_failing_both_ways_is_not_remembered_and_a_key_refusal_is_not_asked_twice()
    {
        var server = Serve(HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ElevenLabsClient().SynthesizeAsync(Profile("key-bad"), "Green flag!"));
        Assert.Equal(2, server.Urls.Count);
        Assert.True(ElevenLabsClient.AsksNotToLog("key-bad"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ElevenLabsClient().SynthesizeAsync(Profile("key-bad"), "Green flag!"));
        Assert.Equal(3, server.Urls.Count);
        Assert.True(ElevenLabsClient.AsksNotToLog("key-bad"));
    }

    // ------------------------------------------------------------------
    // Who is a junior
    // ------------------------------------------------------------------

    private static DriverProfile Junior() => new() { Transponder = "T1", DriverName = "Sam Parker", AnnouncerName = "Zhock", IsJunior = true };

    private static DriverProfile Adult() => new() { Transponder = "T2", DriverName = "Alex Jones" };

    private static PublicPrivacy Register() => PublicPrivacy.From(new[] { Junior(), Adult() });

    [Fact]
    public void A_junior_is_found_by_any_name_they_go_by()
    {
        var register = Register();

        Assert.True(register.MentionsJunior("Sam Parker takes the lead!"));
        Assert.True(register.MentionsJunior("Parker is through."));
        Assert.True(register.MentionsJunior("Here comes Zhock"));
        Assert.False(register.MentionsJunior("Alex Jones takes the lead!"));
        Assert.False(register.MentionsJunior(""));
        Assert.False(PublicPrivacy.Empty.MentionsJunior("Sam Parker"));
    }

    [Fact]
    public void A_removed_juniors_names_still_count()
    {
        var register = PublicPrivacy.From(
            new[] { Adult() },
            new[] { new PrivacyRetainedDriver { Name = "Sam Parker", Transponders = new List<string> { "T1" }, Junior = true } });

        Assert.True(register.MentionsJunior("Sam is on the move"));
    }

    // ------------------------------------------------------------------
    // Voice: lines naming a junior stay on this PC
    // ------------------------------------------------------------------

    [Fact]
    public void Offline_voices_for_juniors_is_off_by_default()
    {
        Assert.False(new AppSettings().OfflineVoicesForJuniors);
    }

    [Fact]
    public void A_line_naming_a_junior_is_kept_here_only_with_the_switch_on()
    {
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load())
        {
            JuniorRegister = Register,
        };

        Assert.False(announcer.KeepsJuniorLineHere("Sam Parker takes the lead!"));

        var on = true;
        announcer.JuniorsStayOnThisPc = () => on;

        Assert.True(announcer.KeepsJuniorLineHere("Sam Parker takes the lead!"));
        Assert.False(announcer.KeepsJuniorLineHere("Alex Jones takes the lead!"));

        on = false;
        Assert.False(announcer.KeepsJuniorLineHere("Sam Parker takes the lead!"));
    }

    [Fact]
    public void Every_way_a_line_leaves_for_a_cloud_voice_asks_first()
    {
        var synthesis = Source("AnnouncementService.Synthesis.cs");
        var junior = synthesis.IndexOf("if (KeepsJuniorLineHere(beforeLetter))", StringComparison.Ordinal);
        var cloud = synthesis.IndexOf("text = ForCloudVoice(beforeLetter);", StringComparison.Ordinal);
        Assert.True(junior > 0 && junior < cloud, "RenderLineAsync asks before the line goes off this PC");

        Assert.Contains("if (KeepsJuniorLineHere(text))", Source("AnnouncementService.Streaming.cs"));
        Assert.Contains("if (lines.Any(l => KeepsJuniorLineHere(l.Text)))", Source("AnnouncementService.DriverRelease.cs"));
        Assert.Contains("if (KeepsJuniorLineHere(line.Text))", Source("AnnouncementService.PreparedLines.cs"));

        // And a junior's line is never handed to a cloud voice when Kokoro can't speak.
        var juniors = Source("AnnouncementService.Juniors.cs");
        Assert.DoesNotContain("SynthesizeGated", juniors);
        Assert.DoesNotContain("RenderWithHeatCallerAsync", juniors);
    }

    // ------------------------------------------------------------------
    // Commentary: about a junior, written on this PC
    // ------------------------------------------------------------------

    private CommentaryService Desk(bool on, ICommentaryBrain client) =>
        new(ProfileStore.Load(), new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60, OfflineVoicesForJuniors = on })
        {
            Client = client,
            JuniorRegister = Register,
        };

    [Fact]
    public void A_prompt_is_about_a_junior_when_it_names_one_or_says_so()
    {
        var off = Desk(false, new Brain("x"));
        var on = Desk(true, new Brain("x"));

        Assert.False(off.AboutAJuniorKeptHere("system", "Sam Parker won the heat."));
        Assert.True(on.AboutAJuniorKeptHere("system", "Sam Parker won the heat."));
        Assert.True(on.AboutAJuniorKeptHere(CommentaryService.JuniorRuleLead + " - a child.", "Facts."));
        Assert.False(on.AboutAJuniorKeptHere("system", "Alex Jones won the heat."));
    }

    [Fact]
    public async Task Introductions_with_a_junior_in_them_are_written_on_this_pc_and_never_sent_to_the_relay()
    {
        var relay = new Brain("From the relay.");
        var local = new Brain("From this PC.");

        // Unticked fallback: even so, a junior's line goes to this PC.
        var desk = Desk(true, new FallbackCommentaryBrain(relay, local, onAnyFailure: false, monthSpent: () => false));

        var line = await desk.BuildDriverIntroductionsAsync(
            "A-Main",
            new[] { new IntroDriver("T1", 1, "Sam Parker", "Zhock"), new IntroDriver("T2", 2, "Alex Jones", "Alex Jones") });

        Assert.Equal(0, relay.Asked);
        Assert.Equal(1, local.Asked);
        Assert.NotNull(line);
        Assert.False(FallbackCommentaryBrain.LocalOnly);
    }

    [Fact]
    public async Task With_the_switch_off_the_relay_writes_as_before()
    {
        var relay = new Brain("From the relay.");
        var local = new Brain("From this PC.");
        var desk = Desk(false, new FallbackCommentaryBrain(relay, local, onAnyFailure: false, monthSpent: () => false));

        await desk.BuildDriverIntroductionsAsync(
            "A-Main",
            new[] { new IntroDriver("T1", 1, "Sam Parker", "Zhock"), new IntroDriver("T2", 2, "Alex Jones", "Alex Jones") });

        Assert.Equal(1, relay.Asked);
        Assert.Equal(0, local.Asked);
    }

    [Fact]
    public async Task A_colour_segment_about_a_junior_is_left_unsaid_rather_than_written_here()
    {
        var relay = new Brain("From the relay.");
        var local = new Brain("From this PC.");
        var pair = new FallbackCommentaryBrain(relay, local);

        FallbackCommentaryBrain.LocalOnly = true;
        FallbackCommentaryBrain.RelayOnly = true;

        try
        {
            Assert.Null(await pair.GenerateAsync("system", "facts", 40));
        }
        finally
        {
            FallbackCommentaryBrain.LocalOnly = false;
            FallbackCommentaryBrain.RelayOnly = false;
        }

        Assert.Equal(0, relay.Asked);
        Assert.Equal(0, local.Asked);
    }

    [Fact]
    public async Task The_relay_alone_sends_nothing_about_a_junior()
    {
        var said = new List<string>();
        var relay = new RelayCommentaryClient(new AppSettings());
        relay.StatusMessage += said.Add;

        FallbackCommentaryBrain.LocalOnly = true;

        try
        {
            Assert.Null(await relay.GenerateAsync("system", "Sam Parker", 40));
            Assert.Null(await relay.GenerateAsync("system", "Sam Parker", 40));
        }
        finally
        {
            FallbackCommentaryBrain.LocalOnly = false;
        }

        Assert.Single(said);
        Assert.Contains("junior", said[0]);
    }

    // ------------------------------------------------------------------

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    private static FakeElevenLabs Serve(params HttpStatusCode[] answers)
    {
        var server = new FakeElevenLabs(answers);
        ElevenLabsClient.Http = new HttpClient(server) { BaseAddress = new Uri("https://api.elevenlabs.io/") };
        return server;
    }

    private sealed class FakeElevenLabs(HttpStatusCode[] answers) : HttpMessageHandler
    {
        private int _next;

        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Urls)
            {
                Urls.Add(request.RequestUri!.PathAndQuery);
            }

            var status = _next < answers.Length ? answers[_next++] : HttpStatusCode.OK;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = status == HttpStatusCode.OK ? new ByteArrayContent(new byte[] { 1, 2, 3 }) : new StringContent("{\"detail\":\"no\"}"),
            });
        }
    }

    private sealed class Brain(string answer) : ICommentaryBrain
    {
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public event Action<string>? StatusMessage;

        public string Describe => "a test brain";

        public Task<string?> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature = 0.7,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            Interlocked.Increment(ref _asked);
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult<string?>(answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("OK");
    }
}

/// <summary>Tests that swap ElevenLabsClient's speech client or its no-logging switch run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ElevenLabsHttpCollection
{
    public const string Name = "ElevenLabs speech client";
}
