using System.Net;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Race calls in the HeatCaller voice played while the relay is still
/// sending them (11 Nov 2026) - the streaming tick on the Voice page for the
/// clubs with no ElevenLabs key of their own, which is most of them.
/// </summary>
public sealed class HeatCallerStreamingTests : IDisposable
{
    private readonly HttpClient _realHttp = HeatCallerVoiceClient.Http;

    public void Dispose() => HeatCallerVoiceClient.Http = _realHttp;

    private static AppSettings RelaySettings() => new()
    {
        VoiceRelayBaseUrl = "http://localhost:8787",
        VoiceRelayClub = "testclub",
        VoiceRelayToken = "club-token",
    };

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

    /// <summary>A relay that answers every line with these bytes and these headers, and keeps what it was sent.</summary>
    private sealed class Relay(byte[] answer, string contentType, params (string Name, string Value)[] headers) : HttpMessageHandler
    {
        public List<string> Sent { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(answer),
                RequestMessage = request,
            };

            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        }
    }

    [Fact]
    public async Task A_streamed_line_is_asked_for_as_a_stream_and_read_as_it_comes()
    {
        var relay = new Relay(new byte[] { 1, 2, 3, 4 }, "audio/pcm", ("x-voice-format", "pcm_22050"), ("x-voice-used", "20"), ("x-voice-allowance", "1000"));
        HeatCallerVoiceClient.Http = new HttpClient(relay);

        var answer = await new HeatCallerVoiceClient(RelaySettings()).SpeakStreamAsync("clubhouse", "Dee Ploy, six eight seven!", shared: false);

        Assert.Null(answer.Audio);
        Assert.NotNull(answer.Stream);

        using (answer.Stream)
        {
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await answer.Stream!.Content.ReadAsByteArrayAsync());
        }

        Assert.Contains("\"stream\":true", Assert.Single(relay.Sent));
    }

    [Fact]
    public async Task A_relay_that_answers_whole_hands_back_the_clip()
    {
        // A line the relay already had, or a relay deployed before streaming.
        var relay = new Relay(Encoding.UTF8.GetBytes("ID3 pretend mp3"), "audio/mpeg", ("x-voice-cache", "hit"));
        HeatCallerVoiceClient.Http = new HttpClient(relay);

        var answer = await new HeatCallerVoiceClient(RelaySettings()).SpeakStreamAsync("clubhouse", "Dee Ploy, six eight seven!", shared: false);

        Assert.Null(answer.Stream);
        Assert.Equal("ID3 pretend mp3", Encoding.UTF8.GetString(answer.Audio!));
        Assert.True(answer.Cached);
    }

    [Fact]
    public async Task A_whole_line_is_never_asked_for_as_a_stream()
    {
        var relay = new Relay(Encoding.UTF8.GetBytes("ID3 pretend mp3"), "audio/mpeg");
        HeatCallerVoiceClient.Http = new HttpClient(relay);

        await new HeatCallerVoiceClient(RelaySettings()).SpeakAsync("clubhouse", "Green flag!", shared: true);

        Assert.DoesNotContain("stream", Assert.Single(relay.Sent));
    }

    [Fact]
    public async Task A_streamed_line_passes_the_same_checks_before_anything_is_sent()
    {
        var relay = new Relay(new byte[] { 1 }, "audio/pcm", ("x-voice-format", "pcm_22050"));
        HeatCallerVoiceClient.Http = new HttpClient(relay);
        var client = new HeatCallerVoiceClient(RelaySettings());

        await Assert.ThrowsAsync<HeatCallerVoiceException>(() => client.SpeakStreamAsync("", "Hello", shared: false));
        await Assert.ThrowsAsync<HeatCallerVoiceException>(() => client.SpeakStreamAsync("clubhouse", new string('x', HeatCallerVoiceClient.MaxCharacters + 1), shared: false));
        Assert.Empty(relay.Sent);
    }

    [Fact]
    public void The_HeatCaller_voice_learns_its_own_level()
    {
        VoiceLevelMemory.Forget();

        // The ElevenLabs fields of a HeatCaller profile are not the voice heard.
        var heatCaller = new VoiceProfile { Engine = VoiceEngine.HeatCaller, HeatCallerVoice = "clubhouse", VoiceId = "left-over", ModelId = "eleven_v4_turbo" };
        var elevenLabs = new VoiceProfile { Engine = VoiceEngine.ElevenLabs, VoiceId = "left-over", ModelId = "eleven_v4_turbo" };

        Assert.NotEqual(VoiceLevelMemory.KeyFor(heatCaller), VoiceLevelMemory.KeyFor(elevenLabs));

        VoiceLevelMemory.Learn(heatCaller, VoiceLevel.TargetDb - 6);
        Assert.NotNull(VoiceLevelMemory.GainFor(heatCaller));
        Assert.Null(VoiceLevelMemory.GainFor(elevenLabs));

        VoiceLevelMemory.Forget();
    }

    [Fact]
    public void The_HeatCaller_voice_streams_under_its_own_stand_in_rules()
    {
        var streaming = Source("AnnouncementService.Streaming.cs");

        Assert.Contains("VoiceEngine.HeatCaller => HeatCallerMayStream(profile, ForCloudVoice(text)),", streaming);

        foreach (var rule in new[] { "!profile.HeatCallerReady", "HeatCallerSavedLinesPassedOver(profile)", "HeatCallerConsentWithheld(profile)", "IsFixedLine(cloudText)", "_heatCallerReason is null && !_heatCallerAllowanceSpent" })
        {
            Assert.Contains(rule, streaming);
        }

        // A whole answer is handed on, not asked for twice.
        Assert.Contains("OfferRenderedAhead(RenderAheadKey(profile, text), Task.FromResult(new SynthesisResult(audio, LineSource.HeatCaller)));", streaming);

        // Its whole clips teach the level its streams are played at.
        var queue = Source("AnnouncementService.VoiceQueue.cs");
        Assert.Contains("&& synthesis.Source is LineSource.ElevenLabs or LineSource.HeatCaller && voice is null)", queue);

        // And the tick is there for it on the Voice page.
        var page = Source("VoiceSettingsControl.cs");
        Assert.Contains("_chkStreamLiveCalls.Visible = _rdoElevenLabs.Checked || _rdoHeatCaller.Checked;", page);
    }

    [Fact]
    public void A_streamed_line_is_kept_once_all_of_it_has_arrived()
    {
        var streaming = Source("AnnouncementService.Streaming.cs");
        var loopEnd = streaming.IndexOf("answered();", StringComparison.Ordinal);
        var kept = streaming.IndexOf("keep(all.ToArray());", StringComparison.Ordinal);
        var cutOff = streaming.IndexOf("catch (OperationCanceledException) when (stop.IsCancellationRequested)", StringComparison.Ordinal);

        Assert.True(loopEnd > 0 && kept > loopEnd && kept < cutOff);
    }
}
