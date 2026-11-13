using System.Collections.Concurrent;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;

namespace HeatCaller;

/// <summary>How to reach one of the decoders StreamDecoderClient runs.</summary>
public sealed record StreamDecoderOptions
{
    /// <summary>Which decoder (DecoderConnection, 3 and up).</summary>
    public required int Decoder { get; init; }

    public required DecoderTransport Transport { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public string ComPort { get; init; } = string.Empty;

    public int Baud { get; init; }

    /// <summary>The club's transponders, for a simulated decoder to lap with - see SimulatedDecoder.</summary>
    public IReadOnlyList<string> SimulatedTransponders { get; init; } = Array.Empty<string>();

    /// <summary>Where the connection goes, for the status line and the console.</summary>
    public string Describe() => Transport switch
    {
        DecoderTransport.Serial => $"{ComPort} at {Baud} baud",
        DecoderTransport.Network => $"{Host}:{Port}",
        _ => "the simulated decoder",
    };
}

/// <summary>
/// Runs every decoder from 3 up - MyLaps P3, ZRound, Cano / RCHourglass,
/// OpenStint and the serial EasyLap - over a serial port, the network,
/// ZeroMQ or a simulated decoder. One decoder at a time, as the line.
/// </summary>
/// <remarks>
/// <para>
/// WHY (1 Nov 2026, decoder support plan): the five differ only in their
/// bytes, which DecoderProtocols reads and StreamDecoderReader puts on the
/// app's clock. Everything else is here, once: connecting on a thread of
/// its own, so the window never waits on a port or a socket; reconnecting
/// every few seconds when the link drops, until Disconnect; and a raw
/// capture of every byte, so the first night on a real decoder can be read
/// afterwards, as the I-Lap's capture was.
/// </para>
/// <para>
/// The rules the I-Lap and EasyLap clients learned the hard way are kept:
/// Connect hands back at once with the state Connecting; a connection
/// overtaken by a Disconnect or a later Connect says nothing and keeps
/// nothing (the generation); ports are closed on a thread of their own,
/// since disposing a pulled USB adapter can hang for good; and passes are
/// raised with no lock held.
/// </para>
/// </remarks>
public sealed class StreamDecoderClient : IDisposable
{
    /// <summary>How long a dropped link waits before the next try.</summary>
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(3);

    /// <summary>How long a connection may hear nothing before the console says so, once.</summary>
    private static readonly TimeSpan NothingReceivedWarning = TimeSpan.FromSeconds(10);

    /// <summary>How long a network connect may take before it is given up.</summary>
    private const int NetworkConnectTimeoutMs = 5000;

    /// <summary>How long each wait for bytes lasts before looking again whether to stop.</summary>
    private const int ReceiveTimeoutMs = 100;

    private readonly string _captureFolder;

    /// <summary>Guards the generation, the live link and the state.</summary>
    private readonly object _gate = new();

    /// <summary>Bumped by every Connect and Disconnect - see the remarks.</summary>
    private int _generation;

    /// <summary>The open port, socket or simulator, so Disconnect can close it. Guarded by _gate.</summary>
    private IDecoderLink? _live;

    /// <summary>The last status published, so a link failing the same way every few seconds says so once.</summary>
    private (DecoderConnectionState State, string Message)? _lastStatus;

    public event Action<DecoderConnectionState, string>? StatusChanged;
    public event Action<string>? RawMessage;
    public event Action<DirectLapEvent>? LapEventReceived;

    public DecoderConnectionState State { get; private set; } = DecoderConnectionState.Disconnected;

    /// <summary>The decoder connected or being connected (DecoderConnection), or 0 when none.</summary>
    public int Decoder { get; private set; }

    /// <param name="captureFolder">
    /// Where the raw capture files go - AppPaths.Root in the app, as the
    /// I-Lap's. A test leaves it out and captures under the temp folder.
    /// </param>
    public StreamDecoderClient(string? captureFolder = null)
    {
        _captureFolder = captureFolder ?? Path.Combine(Path.GetTempPath(), "HeatCaller", "decoder-capture");
    }

    /// <summary>
    /// Starts connecting, and hands back at once with <see cref="State"/>
    /// Connecting. A link that drops is tried again every few seconds until
    /// <see cref="Disconnect"/> or another Connect.
    /// </summary>
    public void Connect(StreamDecoderOptions options)
    {
        int generation;
        IDecoderLink? old;

        lock (_gate)
        {
            generation = ++_generation;
            old = _live;
            _live = null;
            Decoder = options.Decoder;
            _lastStatus = null;
        }

        CloseInBackground(old);
        PublishIfCurrent(generation, DecoderConnectionState.Connecting, $"Connecting to the {DecoderConnection.ShortName(options.Decoder)} decoder on {options.Describe()}...");

        new Thread(() => Run(options, generation))
        {
            IsBackground = true,
            Name = $"{DecoderConnection.ShortName(options.Decoder)} decoder",
        }.Start();
    }

    public void Disconnect()
    {
        IDecoderLink? old;

        lock (_gate)
        {
            _generation++;
            old = _live;
            _live = null;
            _lastStatus = null;
        }

        CloseInBackground(old);

        if (State != DecoderConnectionState.Disconnected)
        {
            State = DecoderConnectionState.Disconnected;
            RaiseStatus(DecoderConnectionState.Disconnected, "Disconnected.");
        }
    }

    public void Dispose() => Disconnect();

    private bool IsCurrent(int generation) => Volatile.Read(ref _generation) == generation;

    /// <summary>The connection thread: connect, read until the link drops, wait, and again - while this is still the connection wanted.</summary>
    private void Run(StreamDecoderOptions options, int generation)
    {
        while (IsCurrent(generation))
        {
            string? failure = null;

            try
            {
                RunOnce(options, generation);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            if (!IsCurrent(generation))
            {
                return;
            }

            try
            {
                PublishIfCurrent(generation, DecoderConnectionState.Error,
                    $"The {DecoderConnection.ShortName(options.Decoder)} decoder on {options.Describe()}: {failure ?? "the connection ended"}. Trying again every {RetryEvery.TotalSeconds:0} seconds - press Disconnect to stop.");
            }
            catch
            {
                // A subscriber threw. Nobody waits on this thread, so nothing may escape it.
            }

            var waited = System.Diagnostics.Stopwatch.StartNew();

            while (waited.Elapsed < RetryEvery && IsCurrent(generation))
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>One connection, from opening the link until it fails or is no longer wanted. Throws when the link fails.</summary>
    private void RunOnce(StreamDecoderOptions options, int generation)
    {
        var protocol = DecoderProtocols.For(options.Decoder);
        var link = Open(options);

        lock (_gate)
        {
            if (_generation != generation)
            {
                // Opened for nobody: a Disconnect, or a later Connect, while the port or socket had us.
                CloseInBackground(link);
                return;
            }

            _live = link;
        }

        using var capture = new RawCapture(_captureFolder, options.Decoder, protocol.IsText, Log);
        capture.Start(options.Describe());

        var reader = new StreamDecoderReader(options.Decoder, protocol, Log);

        try
        {
            if (protocol.Greeting.Length > 0)
            {
                link.Send(protocol.Greeting);
                capture.Add("sent: ", protocol.Greeting);
            }

            if (!PublishIfCurrent(generation, DecoderConnectionState.Connected,
                    $"Connected to the {DecoderConnection.ShortName(options.Decoder)} decoder on {options.Describe()}. Every byte it sends is also captured to {capture.FileName}."))
            {
                return;
            }

            var quiet = System.Diagnostics.Stopwatch.StartNew();
            var heardAnything = false;
            var warned = false;

            while (IsCurrent(generation))
            {
                var bytes = link.Receive(ReceiveTimeoutMs);

                if (bytes is null)
                {
                    if (!heardAnything && !warned && quiet.Elapsed > NothingReceivedWarning)
                    {
                        warned = true;
                        Log($"[decoder] Nothing has come from {options.Describe()} in {NothingReceivedWarning.TotalSeconds:0} seconds. "
                            + (options.Decoder == DecoderConnection.Cano
                                ? "A Cano sends nothing until a car crosses - drive one over the loop."
                                : "Check this is the decoder's address or port, and the decoder's own settings."));
                    }

                    continue;
                }

                var arrivedMs = TimingClock.NowMs;

                if (!heardAnything)
                {
                    heardAnything = true;
                    Log($"[decoder] The {DecoderConnection.ShortName(options.Decoder)} decoder is talking.");
                }

                capture.Add(string.Empty, bytes);

                foreach (var pass in reader.Feed(bytes, arrivedMs))
                {
                    if (!IsCurrent(generation))
                    {
                        return;
                    }

                    // Marked, so a rehearsal's made-up laps never become a
                    // real transponder's health history (Bug Hunt 15, B2).
                    LapEventReceived?.Invoke(options.Transport == DecoderTransport.Simulated ? pass.AsSimulated() : pass);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_live, link))
                {
                    _live = null;
                }
            }

            CloseInBackground(link);
        }
    }

    /// <summary>Opens the link. On the connection thread, where it may block as long as the port or network likes.</summary>
    private static IDecoderLink Open(StreamDecoderOptions options) => options.Transport switch
    {
        DecoderTransport.Serial => SerialLink.Open(options.ComPort, options.Baud),
        DecoderTransport.Network when options.Decoder == DecoderConnection.OpenStint => ZeroMqLink.Open(options.Host, options.Port),
        DecoderTransport.Network => TcpLink.Open(options.Host, options.Port),
        _ => new SimulatedLink(options.Decoder, options.SimulatedTransponders),
    };

    /// <summary>Closes a link on a thread of its own - closing a pulled USB adapter can hang for good. Never throws.</summary>
    private static void CloseInBackground(IDecoderLink? link)
    {
        if (link is null)
        {
            return;
        }

        new Thread(() =>
        {
            try
            {
                link.Dispose();
            }
            catch
            {
                // Nothing is reading it any more.
            }
        })
        {
            IsBackground = true,
            Name = "Decoder close",
        }.Start();
    }

    /// <summary>
    /// Sets and announces the state if <paramref name="generation"/> is
    /// still the connection wanted. The same state and message as last time
    /// are not announced again. True if this generation is current.
    /// </summary>
    private bool PublishIfCurrent(int generation, DecoderConnectionState state, string message)
    {
        lock (_gate)
        {
            if (_generation != generation)
            {
                return false;
            }

            State = state;

            if (_lastStatus == (state, message))
            {
                return true;
            }

            _lastStatus = (state, message);
        }

        RaiseStatus(state, message);
        return true;
    }

    private void RaiseStatus(DecoderConnectionState state, string message)
    {
        StatusChanged?.Invoke(state, message);
        Log($"[status] {message}");
    }

    private void Log(string line) => RawMessage?.Invoke($"{AppTime.LogStampNow()}  {line}");

    // --- The links -------------------------------------------------------------------

    /// <summary>A port, socket or simulator, read by one thread.</summary>
    private interface IDecoderLink : IDisposable
    {
        /// <summary>The next bytes, waiting at most <paramref name="timeoutMs"/> - null when none came. Throws when the link has failed or closed.</summary>
        byte[]? Receive(int timeoutMs);

        void Send(byte[] bytes);
    }

    private sealed class SerialLink : IDecoderLink
    {
        private readonly SerialPort _port;
        private readonly byte[] _buffer = new byte[4096];

        private SerialLink(SerialPort port) => _port = port;

        public static SerialLink Open(string portName, int baud)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new InvalidOperationException("no COM port picked");
            }

            var port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = ReceiveTimeoutMs,
                WriteTimeout = 1000,

                // Raised, as a terminal program does - many USB serial
                // decoders stay silent until the host asserts DTR (the I-Lap
                // did, 3 Oct 2026).
                DtrEnable = true,
                RtsEnable = true,
            };

            try
            {
                port.Open();
            }
            catch (Exception ex)
            {
                port.Dispose();
                throw new IOException($"couldn't open {portName}: {ex.Message} If another timing program has the port, close it first.", ex);
            }

            return new SerialLink(port);
        }

        public byte[]? Receive(int timeoutMs)
        {
            try
            {
                var read = _port.Read(_buffer, 0, _buffer.Length);
                return read > 0 ? _buffer[..read] : null;
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        public void Send(byte[] bytes) => _port.Write(bytes, 0, bytes.Length);

        public void Dispose() => _port.Dispose();
    }

    /// <summary>
    /// Has Windows ask a network decoder "still there?" after a few quiet
    /// seconds, and fail the socket when it stops answering.
    ///
    /// WHY (bug hunt 17): nothing is sent after the greeting, so a decoder
    /// that lost power, or a switch that dropped, was never noticed - no
    /// close ever arrives, the read just waits. The state stayed Connected
    /// and the retry never ran, with the decoder back up and waiting for a
    /// new connection. About ten seconds of silence from a dead peer now
    /// ends the connection, and Run tries again as it does for any other
    /// drop. A decoder that is merely quiet (a Cano between cars) still
    /// answers, and is left alone.
    /// </summary>
    internal static void WatchForADeadPeer(Socket socket)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);
        }
        catch (SocketException)
        {
            // A Windows too old for these: connected as before, without the watch.
        }
    }

    private sealed class TcpLink : IDecoderLink
    {
        private readonly TcpClient _client;
        private readonly Socket _socket;
        private readonly byte[] _buffer = new byte[8192];

        private TcpLink(TcpClient client)
        {
            _client = client;
            _socket = client.Client;
        }

        public static TcpLink Open(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535)
            {
                throw new InvalidOperationException("no address set - type the decoder's IP address and port");
            }

            var client = new TcpClient { NoDelay = true };

            try
            {
                var connecting = client.ConnectAsync(host.Trim(), port);

                if (!connecting.Wait(NetworkConnectTimeoutMs))
                {
                    throw new TimeoutException($"no answer from {host}:{port} in {NetworkConnectTimeoutMs / 1000} seconds - check the address, and that this PC is on the decoder's network");
                }
            }
            catch (AggregateException ex) when (ex.InnerException is not null)
            {
                client.Dispose();
                throw new IOException(ex.InnerException.Message, ex.InnerException);
            }
            catch
            {
                client.Dispose();
                throw;
            }

            WatchForADeadPeer(client.Client);
            return new TcpLink(client);
        }

        public byte[]? Receive(int timeoutMs)
        {
            if (!_socket.Poll(timeoutMs * 1000, SelectMode.SelectRead))
            {
                return null;
            }

            var read = _socket.Receive(_buffer);

            // Readable with nothing to read: the decoder closed the connection.
            if (read == 0)
            {
                throw new IOException("the decoder closed the connection");
            }

            return _buffer[..read];
        }

        public void Send(byte[] bytes) => _socket.Send(bytes);

        public void Dispose() => _client.Dispose();
    }

    /// <summary>OpenStint's ZeroMQ publisher - see ZmtpSubscriber. Each message is handed on with a line ending after it.</summary>
    private sealed class ZeroMqLink : IDecoderLink
    {
        private readonly ZmtpSubscriber _subscriber;

        private ZeroMqLink(ZmtpSubscriber subscriber) => _subscriber = subscriber;

        public static ZeroMqLink Open(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535)
            {
                throw new InvalidOperationException("no address set - type the OpenStint PC's IP address and port");
            }

            return new ZeroMqLink(ZmtpSubscriber.Connect(host.Trim(), port, NetworkConnectTimeoutMs));
        }

        public byte[]? Receive(int timeoutMs) =>
            _subscriber.Receive(timeoutMs) is { } message ? Encoding.UTF8.GetBytes(message + "\n") : null;

        public void Send(byte[] bytes)
        {
            // A subscriber sends nothing, and OpenStint expects nothing.
        }

        public void Dispose() => _subscriber.Dispose();
    }

    private sealed class SimulatedLink : IDecoderLink
    {
        private readonly BlockingCollection<byte[]> _sent = new(boundedCapacity: 10_000);
        private readonly SimulatedDecoder _decoder;

        public SimulatedLink(int decoder, IReadOnlyList<string> transponders)
        {
            _decoder = new SimulatedDecoder(decoder, transponders, bytes => _sent.TryAdd(bytes));
            _decoder.Start();
        }

        public byte[]? Receive(int timeoutMs) => _sent.TryTake(out var bytes, timeoutMs) ? bytes : null;

        public void Send(byte[] bytes)
        {
            // The simulator answers ZRound's handshake as it starts.
        }

        public void Dispose()
        {
            _decoder.Dispose();
            _sent.Dispose();
        }
    }

    // --- The raw capture -------------------------------------------------------------

    /// <summary>
    /// Every byte from one connection, written to the day's file for that
    /// decoder on a thread of its own - the shape ILapClient's capture has,
    /// for the same reasons: the reader never waits on the disk, the queue is
    /// bounded, a day's file is capped, and files go after 14 days. Text
    /// decoders are kept as text, binary ones as hex.
    /// </summary>
    private sealed class RawCapture : IDisposable
    {
        private const long MaxBytesPerDay = 20L * 1024 * 1024;
        private const int QueueLimit = 50_000;
        private static readonly TimeSpan Retention = TimeSpan.FromDays(14);

        private readonly string _folder;
        private readonly bool _isText;
        private readonly Action<string> _log;
        private readonly string _path;
        private BlockingCollection<string>? _pending;
        private Thread? _writer;

        public RawCapture(string folder, int decoder, bool isText, Action<string> log)
        {
            _folder = folder;
            _isText = isText;
            _log = log;

            var name = DecoderConnection.ShortName(decoder).ToLowerInvariant().Replace(' ', '-');
            _path = Path.Combine(folder, $"decoder-raw-{name}-{DateTime.Now:yyyy-MM-dd}.log");
        }

        public string FileName => Path.GetFileName(_path);

        public void Start(string where)
        {
            StreamWriter? writer = null;

            try
            {
                Directory.CreateDirectory(_folder);
                DeleteOld();

                // Shared for read, write and delete, so a backup restore is never held up by it.
                var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (stream.Length >= MaxBytesPerDay)
                {
                    writer.Dispose();
                    _log($"[decoder] Today's raw capture is already {stream.Length / (1024 * 1024)} MB - not capturing more today.");
                    return;
                }

                var pending = new BlockingCollection<string>(QueueLimit);
                pending.Add($"--- connected {DateTime.Now:yyyy-MM-dd HH:mm:ss} on {where} ---");

                var started = writer;
                var startBytes = stream.Length;
                _writer = new Thread(() => Write(started, pending, startBytes)) { IsBackground = true, Name = "Decoder capture writer" };
                _pending = pending;
                _writer.Start();
                writer = null;
            }
            catch
            {
                // A capture that can't be written is a loss, not a failure - timing carries on.
                _pending = null;
                writer?.Dispose();
            }
        }

        public void Add(string prefix, byte[] bytes)
        {
            var pending = _pending;

            if (pending is null)
            {
                return;
            }

            var text = _isText ? Printable(bytes) : EasyLapFramer.ToHex(bytes);

            try
            {
                pending.TryAdd($"{AppTime.LogStampNow()}  {prefix}{text}");
            }
            catch
            {
                // Completed by Dispose in the same moment.
            }
        }

        /// <summary>Text as it came, with line endings and other control characters shown rather than acted on.</summary>
        private static string Printable(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length);

            foreach (var b in bytes)
            {
                text.Append(b switch
                {
                    (byte)'\r' => "\\r",
                    (byte)'\n' => "\\n",
                    < 0x20 or >= 0x7F => $"\\x{b:X2}",
                    _ => ((char)b).ToString(),
                });
            }

            return text.ToString();
        }

        private void Write(StreamWriter writer, BlockingCollection<string> pending, long written)
        {
            try
            {
                using (writer)
                {
                    foreach (var line in pending.GetConsumingEnumerable())
                    {
                        var bytes = Encoding.UTF8.GetByteCount(line) + 2;

                        if (written + bytes > MaxBytesPerDay)
                        {
                            writer.WriteLine($"--- capture stopped: file reached {MaxBytesPerDay / (1024 * 1024)} MB ---");
                            _log($"[decoder] Raw capture file reached {MaxBytesPerDay / (1024 * 1024)} MB - capture stopped; timing is unaffected.");
                            return;
                        }

                        writer.WriteLine(line);
                        written += bytes;

                        if (pending.Count == 0)
                        {
                            writer.Flush();
                        }
                    }
                }
            }
            catch
            {
                // The disk went away or filled up. Stop quietly.
            }
            finally
            {
                _pending = null;
            }
        }

        private void DeleteOld()
        {
            var cutoff = DateTime.Now - Retention;

            foreach (var file in Directory.EnumerateFiles(_folder, "decoder-raw-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // In use or no permission - next time.
                }
            }
        }

        /// <summary>Ends the capture, giving the writer a moment to flush - never long, since this can be on the way to the UI.</summary>
        public void Dispose()
        {
            var pending = _pending;
            _pending = null;

            try
            {
                pending?.CompleteAdding();
            }
            catch
            {
                // Already completed.
            }

            _writer?.Join(TimeSpan.FromMilliseconds(250));
        }
    }
}
