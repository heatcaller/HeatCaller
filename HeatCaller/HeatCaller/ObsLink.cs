using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// THE OBS LINK (14 Nov 2026, race video plan Phase 0). OBS 28 and later has
/// a WebSocket server built in (Tools > WebSocket Server Settings, port 4455).
/// This connects to it on the club's network, from its own thread - never the
/// timing thread, and a failure here never touches timing - and listens for
/// one thing: the stream starting and stopping (StreamStateChanged). Each is
/// stamped on this laptop's own clock as it arrives and handed to
/// StreamTonight, which is what lines the races up with the YouTube video.
///
/// Protocol v5: Hello (op 0, with a challenge when OBS has a password),
/// Identify (op 1, rpcVersion 1, subscribed to Outputs events), Identified
/// (op 2), then events (op 5). The password's answer is
/// base64(sha256(base64(sha256(password + salt)) + challenge)).
///
/// It reconnects by itself, every ten seconds, and says how it is in State
/// for the Health page and the Club Website card. OBS's own GetStreamStatus
/// on connecting catches a stream already live when HeatCaller starts.
/// </summary>
public sealed class ObsLink : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly StreamTonight _stream;
    private readonly Action<string>? _log;
    private readonly CancellationTokenSource _stop = new();
    private Task? _run;

    /// <summary>"off", "connecting", "connected", "streaming", or a short problem in words.</summary>
    public string State { get; private set; } = "off";

    public ObsLink(Func<AppSettings> settings, StreamTonight stream, Action<string>? log = null)
    {
        _settings = settings;
        _stream = stream;
        _log = log;
    }

    public void Start()
    {
        _run ??= Task.Run(() => RunAsync(_stop.Token));
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _run?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { /* closing */ }
        _stop.Dispose();
    }

    /// <summary>The answer to OBS's password challenge.</summary>
    public static string AuthAnswer(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    /// <summary>What a StreamStateChanged event says: true started, false stopped, null neither.</summary>
    public static bool? StreamEvent(JsonElement message)
    {
        if (!message.TryGetProperty("op", out var op) || op.GetInt32() != 5
            || !message.TryGetProperty("d", out var d)
            || !d.TryGetProperty("eventType", out var type) || type.GetString() != "StreamStateChanged"
            || !d.TryGetProperty("eventData", out var data) || !data.TryGetProperty("outputState", out var state))
        {
            return null;
        }

        return state.GetString() switch
        {
            "OBS_WEBSOCKET_OUTPUT_STARTED" => true,
            "OBS_WEBSOCKET_OUTPUT_STOPPED" => false,
            _ => null,
        };
    }

    /// <summary>
    /// OBS's answer to GetStreamStatus (op 7): whether it is streaming, and
    /// since when on this laptop's clock (now less outputDuration, when OBS
    /// gives it). Null for any other message.
    /// </summary>
    public static (bool Streaming, DateTime? LiveSinceUtc)? StreamStatus(JsonElement message, DateTime nowUtc)
    {
        if (!message.TryGetProperty("op", out var op) || op.GetInt32() != 7
            || !message.TryGetProperty("d", out var d)
            || !d.TryGetProperty("responseData", out var data)
            || !data.TryGetProperty("outputActive", out var active)
            || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        DateTime? since = active.GetBoolean() && data.TryGetProperty("outputDuration", out var duration) && duration.TryGetDouble(out var ms) && ms >= 0
            ? nowUtc - TimeSpan.FromMilliseconds(ms)
            : null;

        return (active.GetBoolean(), since);
    }

    /// <summary>
    /// What OBS closing the link means. 4009 is obs-websocket's
    /// AuthenticationFailed: a wrong or missing password (bug hunt 19, #23),
    /// said as such rather than "isn't answering".
    /// </summary>
    public static Exception Closed(WebSocketCloseStatus? status) =>
        (int?)status == 4009
            ? new InvalidOperationException("OBS refused the password. Check it matches the one in OBS's WebSocket Server Settings.")
            : new WebSocketException("OBS closed the link.");

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var settings = _settings();

            if (!settings.VideoEnabled || string.IsNullOrWhiteSpace(settings.VideoObsHost))
            {
                State = "off";
                await Delay(TimeSpan.FromSeconds(5), token);
                continue;
            }

            try
            {
                State = "connecting";
                await OneConnectionAsync(settings, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var said = ex is WebSocketException or System.Net.Sockets.SocketException or HttpRequestException
                    ? "OBS isn't answering - is it open, with its WebSocket server on?"
                    : ex.Message;

                if (State != said)
                {
                    _log?.Invoke($"[video] OBS link: {said}");
                }

                State = said;
            }

            await Delay(TimeSpan.FromSeconds(10), token);
        }
    }

    private async Task OneConnectionAsync(AppSettings settings, CancellationToken token)
    {
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("obswebsocket.json");
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(new Uri($"ws://{settings.VideoObsHost.Trim()}:{settings.VideoObsPort}"), connectTimeout.Token);

        using var hello = await ReceiveAsync(socket, token);
        var identify = new Dictionary<string, object> { ["rpcVersion"] = 1, ["eventSubscriptions"] = 64 };

        if (hello.RootElement.GetProperty("d").TryGetProperty("authentication", out var auth))
        {
            identify["authentication"] = AuthAnswer(settings.VideoObsPassword, auth.GetProperty("salt").GetString() ?? "", auth.GetProperty("challenge").GetString() ?? "");
        }

        await SendAsync(socket, new { op = 1, d = identify }, token);
        using var identified = await ReceiveAsync(socket, token);

        if (identified.RootElement.GetProperty("op").GetInt32() != 2)
        {
            throw Closed((WebSocketCloseStatus)4009);
        }

        State = "connected";
        _log?.Invoke("[video] OBS link connected.");

        // A stream already live when HeatCaller started counts from now.
        await SendAsync(socket, new { op = 6, d = new { requestType = "GetStreamStatus", requestId = "hc-status" } }, token);

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using var message = await ReceiveAsync(socket, token);
            var root = message.RootElement;

            if (StreamEvent(root) is { } started)
            {
                if (started) { _stream.Started(DateTime.UtcNow); State = "streaming"; }
                else { _stream.Stopped(DateTime.UtcNow); State = "connected"; }
                _log?.Invoke(started ? "[video] The stream went live." : "[video] The stream stopped.");
            }
            else if (StreamStatus(root, DateTime.UtcNow) is { } status)
            {
                // Bug hunt 19, #6: a stop or restart while the link was down
                // sent no event, so OBS's answer settles the open piece.
                _stream.ObsSays(status.Streaming, status.LiveSinceUtc, DateTime.UtcNow);
                State = status.Streaming ? "streaming" : "connected";
            }
        }
    }

    private static async Task SendAsync(ClientWebSocket socket, object message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }

    private static async Task<JsonDocument> ReceiveAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();

        while (true)
        {
            var got = await socket.ReceiveAsync(buffer, token);

            if (got.MessageType == WebSocketMessageType.Close)
            {
                throw Closed(got.CloseStatus);
            }

            stream.Write(buffer, 0, got.Count);

            if (got.EndOfMessage)
            {
                break;
            }

            if (stream.Length > 1_000_000)
            {
                throw new InvalidOperationException("OBS sent more than expected.");
            }
        }

        return JsonDocument.Parse(stream.ToArray());
    }

    private static async Task Delay(TimeSpan wait, CancellationToken token)
    {
        try { await Task.Delay(wait, token); } catch (OperationCanceledException) { /* closing */ }
    }
}
