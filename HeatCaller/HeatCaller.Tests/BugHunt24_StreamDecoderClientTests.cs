using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the stream decoders' raw capture at its 20 MB day cap: the
/// size was read from a file already closed, which threw, so the "not
/// capturing more today" line was never shown, and the Connected line still
/// said every byte was being captured.
/// </summary>
public sealed class BugHunt24_StreamDecoderClientTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly string _captures = Path.Combine(Path.GetTempPath(), "HeatCaller.Tests", "decoder-capture-" + Guid.NewGuid().ToString("N"));
    private readonly StreamDecoderClient _client;
    private readonly ConcurrentQueue<string> _log = new();
    private readonly ConcurrentQueue<(DecoderConnectionState State, string Message)> _statuses = new();

    public BugHunt24_StreamDecoderClientTests()
    {
        _client = new StreamDecoderClient(_captures);
        _client.RawMessage += _log.Enqueue;
        _client.StatusChanged += (state, message) => _statuses.Enqueue((state, message));
    }

    public void Dispose()
    {
        _client.Dispose();

        try
        {
            Directory.Delete(_captures, recursive: true);
        }
        catch
        {
            // A capture writer still closing - the temp folder can keep it.
        }
    }

    private static void WaitFor(Func<bool> condition, string what)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(waited.Elapsed < Patience, "Timed out waiting for " + what);
            Thread.Sleep(20);
        }
    }

    private static StreamDecoderOptions Network(int decoder, int port) => new()
    {
        Decoder = decoder,
        Transport = DecoderTransport.Network,
        Host = "127.0.0.1",
        Port = port,
    };

    private string ConnectAndGetConnectedMessage()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _client.Connect(Network(DecoderConnection.ZRound, port));

            using var decoder = listener.AcceptTcpClient();
            WaitFor(() => _statuses.Any(s => s.State == DecoderConnectionState.Connected), "Connected");

            var message = _statuses.First(s => s.State == DecoderConnectionState.Connected).Message;
            _client.Disconnect();
            return message;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void BugHunt24_A_capture_already_at_its_day_cap_says_so_and_the_connected_line_does_not_claim_a_capture()
    {
        Directory.CreateDirectory(_captures);
        var file = Path.Combine(_captures, $"decoder-raw-zround-{DateTime.Now:yyyy-MM-dd}.log");

        using (var full = new FileStream(file, FileMode.Create, FileAccess.Write))
        {
            full.SetLength(20L * 1024 * 1024);
        }

        var connected = ConnectAndGetConnectedMessage();

        Assert.Contains(_log, line => line.Contains("not capturing more today") && line.Contains("20 MB"));
        Assert.DoesNotContain("captured to", connected);
        Assert.StartsWith("Connected to the ZRound decoder on 127.0.0.1:", connected);
        Assert.Equal(20L * 1024 * 1024, new FileInfo(file).Length);
    }

    [Fact]
    public void BugHunt24_A_capture_that_started_is_still_named_in_the_connected_line()
    {
        var connected = ConnectAndGetConnectedMessage();

        Assert.Contains($"Every byte it sends is also captured to decoder-raw-zround-{DateTime.Now:yyyy-MM-dd}.log.", connected);
        Assert.DoesNotContain(_log, line => line.Contains("not capturing more today"));
    }

    [Fact]
    public void BugHunt24_The_connected_line_leaves_out_the_capture_when_there_is_none()
    {
        Assert.Equal(
            "Connected to the ZRound decoder on 1.2.3.4:5403.",
            StreamDecoderClient.ConnectedMessage(DecoderConnection.ZRound, "1.2.3.4:5403", null));

        Assert.Equal(
            "Connected to the ZRound decoder on 1.2.3.4:5403. Every byte it sends is also captured to decoder-raw-zround-2026-11-23.log.",
            StreamDecoderClient.ConnectedMessage(DecoderConnection.ZRound, "1.2.3.4:5403", "decoder-raw-zround-2026-11-23.log"));
    }
}
