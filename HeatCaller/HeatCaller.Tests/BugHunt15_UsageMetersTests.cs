using System.Net;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026): with a separate voice relay, one relay not
/// answering used to drop its open top-up requests from the list as soon as
/// the other relay answered with any of its own - so the club saw no pending
/// request and could ask again.
/// </summary>
public sealed class BugHunt15_UsageMetersTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly HttpClient _realRelayHttp = UsageMeters.Http;

    public void Dispose()
    {
        UsageMeters.Http = _realRelayHttp;
        _data.Dispose();
    }

    private const string CommentaryAnswer = """
        { "ok": true,
          "commentary": { "enabled": true, "used": 10, "allowance": 450, "topUp": 0, "resetsAt": "2026-12-01T00:00:00.000Z" },
          "voice": null,
          "requests": [ { "id": "c_1", "meter": "commentary", "askedAt": "2026-11-05T20:00:00.000Z", "note": "", "status": "open" } ] }
        """;

    private const string VoiceAnswer = """
        { "ok": true,
          "commentary": null,
          "voice": { "enabled": true, "used": 500, "allowance": 100000, "topUp": 0, "resetsAt": "2026-12-01T00:00:00.000Z" },
          "requests": [ { "id": "v_1", "meter": "voice", "askedAt": "2026-11-06T20:00:00.000Z", "note": "", "status": "open" } ] }
        """;

    private const string NoRequestsAnswer = """
        { "ok": true,
          "commentary": { "enabled": true, "used": 10, "allowance": 450, "topUp": 0, "resetsAt": "2026-12-01T00:00:00.000Z" },
          "voice": null,
          "requests": [] }
        """;

    private static AppSettings TwoRelays() => new()
    {
        RelayBaseUrl = "https://relay.example.com",
        RelayClub = "acme-rc",
        RelayToken = "club-token",
        VoiceRelayBaseUrl = "https://voice.example.com",
        VoiceRelayClub = "acme-rc",
        VoiceRelayToken = "voice-token",
        CommentaryEnabled = true,
        CommentaryUseRelay = true,
    };

    private static VoiceProfile HeatCallerVoice() => new() { Engine = VoiceEngine.HeatCaller };

    private UsageMeters NewMeters() => new(AppPaths.Root, () => new DateTime(2026, 11, 9, 19, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task The_voice_relay_not_answering_keeps_its_open_request_beside_the_commentary_ones()
    {
        var server = new TwoServers { Commentary = (HttpStatusCode.OK, CommentaryAnswer), Voice = (HttpStatusCode.OK, VoiceAnswer) };
        UsageMeters.Http = new HttpClient(server);

        var meters = NewMeters();
        await meters.RefreshAsync(TwoRelays(), HeatCallerVoice());
        Assert.Equal(new[] { "v_1", "c_1" }, meters.Requests.Select(r => r.Id));

        server.Voice = (HttpStatusCode.BadGateway, "nope");
        await meters.RefreshAsync(TwoRelays(), HeatCallerVoice());

        Assert.Equal(new[] { "v_1", "c_1" }, meters.Requests.Select(r => r.Id));
        Assert.NotNull(meters.LastProblem);
    }

    [Fact]
    public async Task The_commentary_relay_not_answering_keeps_its_open_request_beside_the_voice_ones()
    {
        var server = new TwoServers { Commentary = (HttpStatusCode.OK, CommentaryAnswer), Voice = (HttpStatusCode.OK, VoiceAnswer) };
        UsageMeters.Http = new HttpClient(server);

        var meters = NewMeters();
        await meters.RefreshAsync(TwoRelays(), HeatCallerVoice());

        server.Commentary = (HttpStatusCode.BadGateway, "nope");
        await meters.RefreshAsync(TwoRelays(), HeatCallerVoice());

        Assert.Equal(new[] { "v_1", "c_1" }, meters.Requests.Select(r => r.Id));
        Assert.NotNull(meters.LastProblem);
    }

    [Fact]
    public async Task A_relay_that_answers_with_no_requests_clears_its_own_even_when_the_other_fails()
    {
        var server = new TwoServers { Commentary = (HttpStatusCode.OK, CommentaryAnswer), Voice = (HttpStatusCode.OK, VoiceAnswer) };
        UsageMeters.Http = new HttpClient(server);

        var meters = NewMeters();
        await meters.RefreshAsync(TwoRelays(), HeatCallerVoice());

        // The commentary request was dealt with; the voice relay is down.
        server.Commentary = (HttpStatusCode.OK, NoRequestsAnswer);
        server.Voice = (HttpStatusCode.BadGateway, "nope");
        await meters.RefreshAsync(TwoRelays(), HeatCallerVoice());

        Assert.Equal(new[] { "v_1" }, meters.Requests.Select(r => r.Id));
    }

    /// <summary>The club's relay and its voice relay, each answering one thing until told otherwise.</summary>
    private sealed class TwoServers : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) Commentary { get; set; }

        public (HttpStatusCode Status, string Body) Voice { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = request.RequestUri?.Host == "voice.example.com" ? Voice : Commentary;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
