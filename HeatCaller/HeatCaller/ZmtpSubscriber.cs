using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace HeatCaller;

/// <summary>
/// A ZeroMQ SUB socket over TCP, just enough of it to hear OpenStint: one
/// connection, the NULL security handshake, a subscription to everything,
/// and messages in.
/// </summary>
/// <remarks>
/// <para>
/// WHY (1 Nov 2026): OpenStint publishes only on ZeroMQ. The ready-made
/// .NET library, NetMQ, is LGPL-3.0 and brings six more packages with it;
/// what OpenStint needs is a page of the public wire protocol, ZMTP 3.0
/// (rfc.zeromq.org/spec/23), written here from the spec. The tests run it
/// against NetMQ's own publisher, so it is checked against a real ZeroMQ,
/// not only against itself - NetMQ is a test package and never ships.
/// </para>
/// <para>
/// The wire, in the order it happens: each side sends a 64-byte greeting
/// (0xFF, eight bytes of padding, 0x7F, version 3.0, the mechanism "NULL"
/// padded to 20 bytes, as-server 0, 31 zeros), then a READY command whose
/// properties say what kind of socket it is - "Socket-Type" = "SUB" here,
/// "PUB" from the decoder. A frame is a flags byte (bit 0 more frames to
/// come, bit 1 an 8-byte size rather than 1, bit 2 a command), the size,
/// big-endian, and the body. A subscription, in ZMTP 3.0, is a message
/// whose body is 0x01 and the prefix wanted: 0x01 alone is everything. A
/// PING command is answered with a PONG carrying its context back.
/// </para>
/// <para>
/// Unlike a full ZeroMQ socket this does not reconnect by itself:
/// StreamDecoderClient does, as it does for every other decoder.
/// </para>
/// </remarks>
internal sealed class ZmtpSubscriber : IDisposable
{
    private const int GreetingLength = 64;

    /// <summary>A message longer than this from a timing decoder is not one - the connection is dropped rather than the memory spent.</summary>
    private const long MaxFrameLength = 1024 * 1024;

    /// <summary>
    /// How long a publisher that has finished its handshake may say nothing
    /// before the link is taken for dead. OpenStint sends a status every
    /// second, so ten seconds of silence is not a quiet track.
    /// </summary>
    internal const int DefaultSilenceLimitMs = 10_000;

    private readonly Socket _socket;
    private readonly int _silenceLimitMs;
    private readonly System.Diagnostics.Stopwatch _quiet = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<byte> _in = new();
    private readonly byte[] _buffer = new byte[8192];
    private readonly List<byte[]> _frames = new();
    private bool _greeted;
    private bool _ready;

    private ZmtpSubscriber(Socket socket, int silenceLimitMs)
    {
        _socket = socket;
        _silenceLimitMs = silenceLimitMs;
    }

    /// <summary>The publisher's socket type from its READY, once the handshake is done - "PUB" from OpenStint.</summary>
    public string? PeerSocketType { get; private set; }

    /// <summary>Connects, and sends this side's greeting and READY. The handshake completes as messages are received.</summary>
    public static ZmtpSubscriber Connect(string host, int port, int timeoutMs, int silenceLimitMs = DefaultSilenceLimitMs)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            if (!socket.ConnectAsync(host, port).Wait(timeoutMs))
            {
                throw new TimeoutException($"no answer from {host}:{port} in {timeoutMs / 1000} seconds - check the address, and that OpenStint is running");
            }

            var subscriber = new ZmtpSubscriber(socket, silenceLimitMs);
            socket.Send(Greeting());
            socket.Send(Command("READY", Property("Socket-Type", "SUB")));
            return subscriber;
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            socket.Dispose();
            throw new IOException(ex.InnerException.Message, ex.InnerException);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The next whole message, its frames' text joined by spaces - waiting at
    /// most <paramref name="timeoutMs"/>, null when none came. Throws when the
    /// publisher closes the connection or breaks the protocol, or says
    /// nothing for longer than the silence limit once the handshake is done.
    /// </summary>
    public string? Receive(int timeoutMs)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            if (TakeMessage() is { } message)
            {
                return message;
            }

            var left = timeoutMs - (int)waited.ElapsedMilliseconds;

            if (left <= 0 || !_socket.Poll(left * 1000, SelectMode.SelectRead))
            {
                // WHY (13 Nov 2026, Bug Hunt 17): a publisher that loses
                // power, or whose network drops, never says goodbye - no
                // close reaches this end, so the socket just stays quiet.
                // That used to read as "no cars yet" for ever: the status
                // stayed Connected and nothing reconnected when OpenStint
                // came back. Its once-a-second status is the sign of life,
                // so a long silence is thrown as a failed link and
                // StreamDecoderClient tries again, as it does for a close.
                if (_ready && _quiet.ElapsedMilliseconds > _silenceLimitMs)
                {
                    throw new IOException($"nothing from OpenStint in {_silenceLimitMs / 1000} seconds - the link has gone quiet");
                }

                return null;
            }

            var read = _socket.Receive(_buffer);

            if (read == 0)
            {
                throw new IOException("the OpenStint decoder closed the connection");
            }

            _quiet.Restart();

            _in.AddRange(_buffer.AsSpan(0, read).ToArray());
        }
    }

    /// <summary>Works through what has arrived: the greeting, then frames. A whole message comes back as soon as one is complete.</summary>
    private string? TakeMessage()
    {
        if (!_greeted)
        {
            if (_in.Count < GreetingLength)
            {
                return null;
            }

            ReadGreeting(_in.GetRange(0, GreetingLength).ToArray());
            _in.RemoveRange(0, GreetingLength);
            _greeted = true;
        }

        while (TakeFrame() is { } frame)
        {
            if (frame.IsCommand)
            {
                HandleCommand(frame.Body);
                continue;
            }

            if (!_ready)
            {
                throw new IOException("the publisher sent a message before its READY - not a ZeroMQ peer HeatCaller understands");
            }

            _frames.Add(frame.Body);

            if (frame.More)
            {
                continue;
            }

            var message = string.Join(" ", _frames.Select(f => Encoding.UTF8.GetString(f)));
            _frames.Clear();
            return message;
        }

        return null;
    }

    private static void ReadGreeting(byte[] greeting)
    {
        if (greeting[0] != 0xFF || greeting[9] != 0x7F)
        {
            throw new IOException("the other end is not a ZeroMQ socket - check the port (OpenStint's is 5556)");
        }

        if (greeting[10] < 3)
        {
            throw new IOException($"the publisher speaks ZeroMQ protocol version {greeting[10]}; version 3 or later is needed");
        }

        var mechanism = Encoding.ASCII.GetString(greeting, 12, 20).TrimEnd('\0');

        if (mechanism != "NULL")
        {
            throw new IOException($"the publisher wants ZeroMQ security '{mechanism}'; HeatCaller connects without (NULL)");
        }
    }

    /// <summary>One whole frame from the front of what has arrived, or null when it is not all here yet.</summary>
    private (bool IsCommand, bool More, byte[] Body)? TakeFrame()
    {
        if (_in.Count < 2)
        {
            return null;
        }

        var flags = _in[0];
        var isLong = (flags & 0x02) != 0;
        var header = isLong ? 9 : 2;

        if (_in.Count < header)
        {
            return null;
        }

        long size = isLong
            ? (long)BinaryPrimitives.ReadUInt64BigEndian(_in.GetRange(1, 8).ToArray())
            : _in[1];

        if (size > MaxFrameLength)
        {
            throw new IOException($"the publisher sent a frame of {size} bytes - far more than any decoder message");
        }

        if (_in.Count < header + size)
        {
            return null;
        }

        var body = _in.GetRange(header, (int)size).ToArray();
        _in.RemoveRange(0, header + (int)size);

        return ((flags & 0x04) != 0, (flags & 0x01) != 0, body);
    }

    private void HandleCommand(byte[] body)
    {
        if (body.Length < 1 || body.Length < 1 + body[0])
        {
            throw new IOException("the publisher sent a command HeatCaller can't read");
        }

        var name = Encoding.ASCII.GetString(body, 1, body[0]);
        var data = body.AsSpan(1 + body[0]);

        switch (name.ToUpperInvariant())
        {
            case "READY":
                PeerSocketType = ReadProperty(data, "Socket-Type");
                _ready = true;

                // Everything: an empty subscription matches every message.
                _socket.Send(new byte[] { 0x00, 0x01, 0x01 });
                break;

            case "ERROR":
                var reason = data.Length > 0 ? Encoding.ASCII.GetString(data.Slice(1, Math.Min(data[0], data.Length - 1))) : string.Empty;
                throw new IOException($"the publisher refused the connection: {reason}");

            case "PING":
                // ttl (2 bytes), then a context of up to 16 to send back.
                var context = data.Length > 2 ? data[2..].ToArray() : Array.Empty<byte>();
                _socket.Send(Command("PONG", context));
                break;

            // PONG, and anything newer: not needed to hear passings.
        }
    }

    /// <summary>A property's value from a READY's metadata, or null.</summary>
    private static string? ReadProperty(ReadOnlySpan<byte> metadata, string wanted)
    {
        var at = 0;

        while (at < metadata.Length)
        {
            var nameLength = metadata[at];

            if (at + 1 + nameLength + 4 > metadata.Length)
            {
                break;
            }

            var name = Encoding.ASCII.GetString(metadata.Slice(at + 1, nameLength));
            var valueLength = (int)BinaryPrimitives.ReadUInt32BigEndian(metadata.Slice(at + 1 + nameLength, 4));
            var value = at + 1 + nameLength + 4;

            if (valueLength < 0 || value + valueLength > metadata.Length)
            {
                break;
            }

            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return Encoding.ASCII.GetString(metadata.Slice(value, valueLength));
            }

            at = value + valueLength;
        }

        return null;
    }

    /// <summary>This side's greeting: ZMTP 3.0, NULL, not a server.</summary>
    private static byte[] Greeting()
    {
        var greeting = new byte[GreetingLength];
        greeting[0] = 0xFF;
        greeting[9] = 0x7F;
        greeting[10] = 3;
        greeting[11] = 0;
        Encoding.ASCII.GetBytes("NULL").CopyTo(greeting, 12);
        return greeting;
    }

    /// <summary>A command frame: flags 0x04 (or 0x06 when long), size, then the name's length, the name and the data.</summary>
    private static byte[] Command(string name, byte[] data)
    {
        var body = new List<byte> { (byte)name.Length };
        body.AddRange(Encoding.ASCII.GetBytes(name));
        body.AddRange(data);

        var frame = new List<byte>();

        if (body.Count <= 255)
        {
            frame.Add(0x04);
            frame.Add((byte)body.Count);
        }
        else
        {
            frame.Add(0x06);
            var size = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)body.Count);
            frame.AddRange(size);
        }

        frame.AddRange(body);
        return frame.ToArray();
    }

    /// <summary>One READY property: the name's length and the name, then the value's length (4 bytes, big-endian) and the value.</summary>
    private static byte[] Property(string name, string value)
    {
        var property = new List<byte> { (byte)name.Length };
        property.AddRange(Encoding.ASCII.GetBytes(name));
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)value.Length);
        property.AddRange(size);
        property.AddRange(Encoding.ASCII.GetBytes(value));
        return property.ToArray();
    }

    public void Dispose()
    {
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // Already gone.
        }

        _socket.Dispose();
    }
}
