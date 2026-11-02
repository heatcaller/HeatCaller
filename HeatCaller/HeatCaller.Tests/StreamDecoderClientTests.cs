using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NetMQ;
using NetMQ.Sockets;

namespace HeatCaller.Tests;

/// <summary>
/// StreamDecoderClient end to end, over real sockets on this PC: a P3 and a
/// ZRound decoder played by a TcpListener, OpenStint by NetMQ's own ZeroMQ
/// publisher, and the simulated decoder - from the bytes to the passes the
/// race is handed, with connecting, reconnecting and disconnecting.
/// </summary>
public sealed class StreamDecoderClientTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly string _captures = Path.Combine(Path.GetTempPath(), "HeatCaller.Tests", "decoder-capture-" + Guid.NewGuid().ToString("N"));
    private readonly StreamDecoderClient _client;
    private readonly ConcurrentQueue<DirectLapEvent> _passes = new();
    private readonly ConcurrentQueue<(DecoderConnectionState State, string Message)> _statuses = new();

    public StreamDecoderClientTests()
    {
        _client = new StreamDecoderClient(_captures);
        _client.LapEventReceived += _passes.Enqueue;
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

    private static (TcpListener Listener, int Port) Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    private StreamDecoderOptions Network(int decoder, int port) => new()
    {
        Decoder = decoder,
        Transport = DecoderTransport.Network,
        Host = "127.0.0.1",
        Port = port,
    };

    [Fact]
    public void Connect_hands_back_at_once_and_the_connection_is_made_on_a_thread_of_its_own()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        _client.Connect(Network(DecoderConnection.MyLapsP3, port));

        Assert.True(watch.ElapsedMilliseconds < 1000);
        Assert.Equal(DecoderConnection.MyLapsP3, _client.Decoder);

        using var decoder = listener.AcceptTcpClient();
        WaitFor(() => _client.State == DecoderConnectionState.Connected, "Connected");
    }

    [Fact]
    public void A_P3_decoder_on_the_network_gives_passes_stamped_as_the_line()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        _client.Connect(Network(DecoderConnection.MyLapsP3, port));

        using var decoder = listener.AcceptTcpClient();
        var stream = decoder.GetStream();
        var record = DecoderWire.P3Passing(1, 7404583, 0x0004758ABDE619E8, 51, 16, 0x405FC);

        // Split in two, as a network may deliver it.
        stream.Write(record, 0, 20);
        stream.Flush();
        Thread.Sleep(50);
        stream.Write(record, 20, record.Length - 20);
        stream.Write(DecoderWire.P3Passing(2, 1002, 0x0004758ABDE619E8 + 12_000_000, 51, 16, 0x405FC));

        WaitFor(() => _passes.Count == 2, "two passes");

        var passes = _passes.ToArray();
        Assert.Equal(new[] { "7404583", "1002" }, passes.Select(p => p.TransponderId));
        Assert.All(passes, p => Assert.Equal(DecoderConnection.MyLapsP3, p.Decoder));
        Assert.All(passes, p => Assert.Equal(TimingPoint.Line, p.Point));
        Assert.Equal(16, passes[0].SignalHits);
    }

    [Fact]
    public void A_ZRound_decoder_is_sent_the_handshake_and_race_start_and_its_laps_come_through()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        _client.Connect(Network(DecoderConnection.ZRound, port));

        using var decoder = listener.AcceptTcpClient();
        var stream = decoder.GetStream();

        var heard = new StringBuilder();
        var buffer = new byte[64];
        stream.ReadTimeout = 5000;

        while (heard.Length < 6)
        {
            heard.Append(Encoding.ASCII.GetString(buffer, 0, stream.Read(buffer, 0, buffer.Length)));
        }

        Assert.Equal("%C&%I&", heard.ToString());

        stream.Write(Encoding.ASCII.GetBytes("%A&%T3E8&%L1F4,7D0&"));

        WaitFor(() => _passes.Count == 1, "a lap");
        Assert.Equal("500", _passes.Single().TransponderId);
    }

    [Fact]
    public void A_decoder_that_drops_the_connection_is_connected_again_by_itself()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        _client.Connect(Network(DecoderConnection.MyLapsP3, port));

        using (listener.AcceptTcpClient())
        {
            WaitFor(() => _client.State == DecoderConnectionState.Connected, "the first connection");
        }

        // Closed at the decoder's end: an error, then a new connection.
        WaitFor(() => _statuses.Any(s => s.State == DecoderConnectionState.Error), "the drop to be reported");

        using var again = listener.AcceptTcpClient();
        again.GetStream().Write(DecoderWire.P3Passing(1, 1001, 5_000_000, 50, 10, 1));

        WaitFor(() => _passes.Count == 1, "a pass on the new connection");
        Assert.Equal(DecoderConnectionState.Connected, _client.State);
    }

    [Fact]
    public void A_decoder_that_isn_t_there_is_an_error_said_once_however_often_it_is_retried()
    {
        // A port nothing listens on: taken and let go again.
        var (listener, port) = Listen();
        listener.Stop();

        _client.Connect(Network(DecoderConnection.MyLapsP3, port));

        WaitFor(() => _client.State == DecoderConnectionState.Error, "the error");
        Thread.Sleep(3500); // past one retry

        Assert.Single(_statuses, s => s.State == DecoderConnectionState.Error);
    }

    [Fact]
    public void Disconnect_stops_the_connection_and_nothing_more_is_heard_from_it()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        _client.Connect(Network(DecoderConnection.MyLapsP3, port));
        using var decoder = listener.AcceptTcpClient();
        WaitFor(() => _client.State == DecoderConnectionState.Connected, "Connected");

        _client.Disconnect();
        Assert.Equal(DecoderConnectionState.Disconnected, _client.State);

        try
        {
            decoder.GetStream().Write(DecoderWire.P3Passing(1, 1001, 5_000_000, 50, 10, 1));
        }
        catch (IOException)
        {
            // Already closed at our end - which is the point.
        }

        Thread.Sleep(300);
        Assert.Empty(_passes);
        Assert.Equal(DecoderConnectionState.Disconnected, _client.State);
    }

    [Fact]
    public void Every_byte_a_decoder_sends_is_captured_to_the_day_s_file()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        _client.Connect(Network(DecoderConnection.ZRound, port));
        using (var decoder = listener.AcceptTcpClient())
        {
            decoder.GetStream().Write(Encoding.ASCII.GetBytes("%L1F4,7D0&\r\n"));
            WaitFor(() => _passes.Count == 1, "a lap");
        }

        _client.Disconnect();

        var file = Path.Combine(_captures, $"decoder-raw-zround-{DateTime.Now:yyyy-MM-dd}.log");
        WaitFor(() => File.Exists(file) && File.ReadAllText(file).Contains("%L1F4,7D0&\\r\\n"), "the capture");
        Assert.Contains("sent: %C&%I&", File.ReadAllText(file));
    }

    [Fact]
    public void OpenStint_is_heard_through_a_real_ZeroMQ_publisher()
    {
        using var publisher = new PublisherSocket();
        var port = publisher.BindRandomPort("tcp://127.0.0.1");

        _client.Connect(Network(DecoderConnection.OpenStint, port));
        WaitFor(() => _client.State == DecoderConnectionState.Connected, "Connected");

        // A publisher drops messages until the subscription reaches it, as
        // ZeroMQ does - so it says the same thing until it is heard.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var time = 1618706341L;

        while (_passes.IsEmpty)
        {
            Assert.True(watch.Elapsed < Patience, "Timed out waiting for OpenStint's pass");
            publisher.SendFrame($"P {time} OPN 1615544 3.50 64 89113 24.3");
            Thread.Sleep(50);
        }

        var pass = _passes.First();
        Assert.Equal("1615544", pass.TransponderId);
        Assert.Equal(64, pass.SignalHits);
        Assert.Equal(DecoderConnection.OpenStint, pass.Decoder);
    }

    [Fact]
    public void The_ZeroMQ_subscriber_reads_a_message_of_several_frames_and_knows_its_publisher()
    {
        using var publisher = new PublisherSocket();
        var port = publisher.BindRandomPort("tcp://127.0.0.1");
        using var subscriber = ZmtpSubscriber.Connect("127.0.0.1", port, 3000);

        string? message = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        while (message is null)
        {
            Assert.True(watch.Elapsed < Patience, "Timed out waiting for the message");
            publisher.SendMoreFrame("S").SendFrame("2000 -41.0 5.08 0 0 3.01");
            message = subscriber.Receive(100);
        }

        Assert.Equal("S 2000 -41.0 5.08 0 0 3.01", message);
        Assert.Equal("PUB", subscriber.PeerSocketType);
    }

    [Fact]
    public void The_ZeroMQ_subscriber_refuses_something_that_is_not_ZeroMQ()
    {
        var (listener, port) = Listen();
        using var _ = listener;

        using var subscriber = ZmtpSubscriber.Connect("127.0.0.1", port, 3000);
        using var notZeroMq = listener.AcceptTcpClient();
        notZeroMq.GetStream().Write(Encoding.ASCII.GetBytes(new string('x', 64)));

        Assert.Throws<IOException>(() =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            while (watch.Elapsed < Patience)
            {
                subscriber.Receive(100);
            }
        });
    }

    [Fact]
    public void A_simulated_decoder_laps_the_club_s_own_cars_through_the_whole_client()
    {
        _client.Connect(new StreamDecoderOptions
        {
            Decoder = DecoderConnection.MyLapsP3,
            Transport = DecoderTransport.Simulated,
            SimulatedTransponders = new[] { "4385919" },
        });

        WaitFor(() => !_passes.IsEmpty, "the simulated car's first pass");

        Assert.Equal("4385919", _passes.First().TransponderId);
        Assert.Equal(DecoderConnection.MyLapsP3, _passes.First().Decoder);
    }
}
