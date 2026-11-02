using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HeatCaller;

/// <summary>
/// Each decoder's records as it sends them, built from the published
/// formats (see DecoderProtocols). The simulated decoder sends these, and
/// the tests build their records with them.
/// </summary>
internal static class DecoderWire
{
    // --- MyLaps / AMB P3 ------------------------------------------------------------

    /// <summary>A P3 PASSING record, escaped and framed, as a decoder sends it.</summary>
    internal static byte[] P3Passing(uint passingNumber, uint transponder, ulong rtcMicroseconds, ushort strength, ushort hits, uint decoderId)
    {
        var fields = new List<byte>();
        Field(fields, 0x01, passingNumber, 4);
        Field(fields, 0x03, transponder, 4);
        Field(fields, 0x04, rtcMicroseconds, 8);
        Field(fields, 0x05, strength, 2);
        Field(fields, 0x06, hits, 2);
        Field(fields, 0x08, 0, 2);
        Field(fields, 0x81, decoderId, 4);
        return P3Record(1, fields);
    }

    /// <summary>A P3 STATUS record: noise, temperature and input voltage, and no clock.</summary>
    internal static byte[] P3Status(ushort noise, ushort temperature, byte voltage, uint decoderId)
    {
        var fields = new List<byte>();
        Field(fields, 0x01, noise, 2);
        Field(fields, 0x07, temperature, 2);
        Field(fields, 0x0C, voltage, 1);
        Field(fields, 0x81, decoderId, 4);
        return P3Record(2, fields);
    }

    /// <summary>Header, fields and end marker, then escaped: 0x8D, 0x8E and 0x8F inside go as 0x8D and the byte plus 0x20.</summary>
    internal static byte[] P3Record(ushort type, IReadOnlyList<byte> fields)
    {
        var length = 10 + fields.Count + 1;
        var plain = new List<byte> { 0x8E, 0x02 };
        Number(plain, (ulong)length, 2);
        Number(plain, 0, 2); // CRC - not checked by the reader, and its algorithm is not published
        Number(plain, 0, 2); // flags
        Number(plain, type, 2);
        plain.AddRange(fields);

        var wire = new List<byte> { 0x8E };

        foreach (var b in plain.Skip(1))
        {
            if (b is 0x8D or 0x8E or 0x8F)
            {
                wire.Add(0x8D);
                wire.Add((byte)(b + 0x20));
            }
            else
            {
                wire.Add(b);
            }
        }

        wire.Add(0x8F);
        return wire.ToArray();
    }

    private static void Field(List<byte> into, byte id, ulong value, int size)
    {
        into.Add(id);
        into.Add((byte)size);
        Number(into, value, size);
    }

    private static void Number(List<byte> into, ulong value, int size)
    {
        for (var i = 0; i < size; i++)
        {
            into.Add((byte)(value >> (8 * i)));
        }
    }

    // --- ZRound open protocol --------------------------------------------------------

    /// <summary>A lap: %L, the transponder in hex, a comma, the race time in hex milliseconds, &amp;.</summary>
    internal static string ZRoundLap(uint transponder, long raceMs) =>
        string.Create(CultureInfo.InvariantCulture, $"%L{transponder:X},{raceMs:X}&");

    /// <summary>The decoder's clock: %T, hex milliseconds, &amp;.</summary>
    internal static string ZRoundClock(long raceMs) =>
        string.Create(CultureInfo.InvariantCulture, $"%T{raceMs:X}&");

    internal const string ZRoundAck = "%A&";

    // --- Cano / RCHourglass -----------------------------------------------------------

    /// <summary>
    /// A passing line: six hex digits of transponder, eight of quarter
    /// milliseconds, then - for RCHourglass - a dash and id, hits, quality,
    /// voltage and temperature. CR LF on the end.
    /// </summary>
    internal static string CanoPassing(uint transponder, long ms, int? hits = null, int quality = 0) =>
        string.Create(CultureInfo.InvariantCulture, $"{transponder & 0xFFFFFF:X6}{(uint)(ms * 4):X8}")
        + (hits is { } h ? string.Create(CultureInfo.InvariantCulture, $"-01{h & 0xFF:X2}{quality & 0xFF:X2}0000") : string.Empty)
        + "\r\n";

    // --- OpenStint -------------------------------------------------------------------

    /// <summary>A passing message, as OpenStint publishes it.</summary>
    internal static string OpenStintPassing(long decoderMs, uint transponder, int hits) =>
        string.Create(CultureInfo.InvariantCulture, $"P {decoderMs} OPN {transponder} -10.00 {hits} 90000 20.0");

    /// <summary>A status message, sent once a second.</summary>
    internal static string OpenStintStatus(long decoderMs) =>
        string.Create(CultureInfo.InvariantCulture, $"S {decoderMs} -40.00 5.08 0 0 1.00");

    // --- EasyLap UART stream ----------------------------------------------------------

    /// <summary>A 13-byte pass: length, sequence, three transponder bytes, 00 00, the clock little-endian, hits, strength.</summary>
    internal static byte[] EasyLapPass(byte sequence, uint transponder, uint deviceMs, byte hits, byte strength) =>
        new byte[]
        {
            0x0D, sequence,
            (byte)(transponder >> 16), (byte)(transponder >> 8), (byte)transponder,
            0x00, 0x00,
            (byte)deviceMs, (byte)(deviceMs >> 8), (byte)(deviceMs >> 16), (byte)(deviceMs >> 24),
            hits, strength,
        };

    /// <summary>An 11-byte heartbeat, shaped as rehearsal 3's were: the clock in bytes 3 to 6.</summary>
    internal static byte[] EasyLapHeartbeat(byte sequence, uint deviceMs) =>
        new byte[]
        {
            0x0B, sequence, 0x83,
            (byte)deviceMs, (byte)(deviceMs >> 8), (byte)(deviceMs >> 16), (byte)(deviceMs >> 24),
            0x14, 0xD0, 0x01, 0x03,
        };
}

/// <summary>
/// A decoder made up inside HeatCaller, sending the real protocol's bytes
/// for a handful of cars lapping - "Simulated" on the Connection page, so a
/// club can rehearse with a decoder type before it owns one, and a support
/// call can be reproduced at home.
/// </summary>
/// <remarks>
/// <para>
/// It drives the whole path a real decoder does, from the protocol's bytes
/// through StreamDecoderReader and the client into the race - only the port
/// or socket is missing. Each car laps in its own steady time with a little
/// scatter; the decoder's clock ticks from wherever it likes, as a real one
/// does, and its heartbeat (for the decoders that have one) goes once a
/// second.
/// </para>
/// <para>
/// The transponders are the club's own where they can be written in the
/// protocol - numbers, or for the EasyLap its six hex digits - so the passes
/// land on real drivers. Otherwise it makes up its own.
/// </para>
/// </remarks>
internal sealed class SimulatedDecoder : IDisposable
{
    private readonly int _decoder;
    private readonly Action<byte[]> _send;
    private readonly List<Car> _cars;
    private readonly Random _random;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly long _clockStartMs;
    private readonly ulong _rtcStartMicroseconds;
    private volatile bool _running;
    private Thread? _thread;
    private uint _passingNumber;
    private byte _sequence;

    private sealed class Car
    {
        public required uint Id { get; init; }
        public required long LapMs { get; init; }
        public long NextPassMs { get; set; }
    }

    /// <param name="decoder">Which decoder to be (DecoderConnection, 3 and up).</param>
    /// <param name="transponders">The club's transponders - those the protocol can't carry are left out.</param>
    /// <param name="send">Given every chunk of bytes the decoder sends. On the simulator's own thread.</param>
    /// <param name="seed">Fixes the cars' lap times, for a test.</param>
    public SimulatedDecoder(int decoder, IEnumerable<string> transponders, Action<byte[]> send, int? seed = null)
    {
        _decoder = decoder;
        _send = send;
        _random = seed is { } s ? new Random(s) : new Random();

        // A decoder has been on for a while before anyone connects to it.
        _clockStartMs = _random.Next(60_000, 3_600_000);
        _rtcStartMicroseconds = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);

        var ids = transponders
            .Select(t => CarryableId(decoder, t))
            .OfType<uint>()
            .Distinct()
            .Take(8)
            .ToList();

        if (ids.Count == 0)
        {
            ids = decoder == DecoderConnection.EasyLapSerial
                ? new List<uint> { 0x84C1C4, 0x84FBD4, 0x8501A2, 0x85172B }
                : new List<uint> { 1001, 1002, 1003, 1004 };
        }

        _cars = ids.Select(id => new Car
        {
            Id = id,
            LapMs = _random.Next(11_000, 17_000),
            NextPassMs = _random.Next(2_000, 9_000),
        }).ToList();
    }

    /// <summary>The cars' transponders, as HeatCaller will show them.</summary>
    public IReadOnlyList<string> Transponders => _cars
        .Select(c => _decoder == DecoderConnection.EasyLapSerial
            ? c.Id.ToString("X6", CultureInfo.InvariantCulture)
            : c.Id.ToString(CultureInfo.InvariantCulture))
        .ToList();

    /// <summary>A club transponder as this protocol would carry it, or null when it can't.</summary>
    internal static uint? CarryableId(int decoder, string transponder)
    {
        var text = transponder.Trim();

        if (decoder == DecoderConnection.EasyLapSerial)
        {
            return text.Length == 6 && DecoderProtocols.IsHex(text)
                ? (uint)DecoderProtocols.ParseHex(text)
                : null;
        }

        if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        // Cano carries six hex digits; the ZRound record without a comma
        // would carry two, but the simulator always sends the comma.
        return decoder == DecoderConnection.Cano && id > 0xFFFFFF ? null : id;
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;

        if (_decoder == DecoderConnection.ZRound)
        {
            // What a decoder says to the handshake HeatCaller sends.
            _send(Encoding.ASCII.GetBytes(DecoderWire.ZRoundAck));
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Simulated decoder" };
        _thread.Start();
    }

    private void Run()
    {
        var nextBeat = 0L;

        while (_running)
        {
            Tick(_clock.ElapsedMilliseconds, ref nextBeat);
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Sends whatever is due at <paramref name="elapsedMs"/> since the
    /// simulator started: a heartbeat once a second, and every car whose
    /// pass has come round. Internal so a test can step it without waiting.
    /// </summary>
    internal void Tick(long elapsedMs, ref long nextBeatMs)
    {
        if (elapsedMs >= nextBeatMs)
        {
            nextBeatMs = elapsedMs + 1000;
            Send(Heartbeat(elapsedMs));
        }

        foreach (var car in _cars)
        {
            if (elapsedMs < car.NextPassMs)
            {
                continue;
            }

            Send(Pass(car, car.NextPassMs));

            // A little scatter round each car's own pace.
            car.NextPassMs += car.LapMs + _random.Next(-400, 600);
        }
    }

    private void Send(byte[]? bytes)
    {
        if (bytes is { Length: > 0 })
        {
            _send(bytes);
        }
    }

    private byte[]? Heartbeat(long elapsedMs)
    {
        var decoderMs = _clockStartMs + elapsedMs;

        return _decoder switch
        {
            DecoderConnection.MyLapsP3 => DecoderWire.P3Status(30, 25, 120, 0x405FC),
            DecoderConnection.ZRound => Encoding.ASCII.GetBytes(DecoderWire.ZRoundClock(elapsedMs)),
            DecoderConnection.OpenStint => Encoding.ASCII.GetBytes(DecoderWire.OpenStintStatus(decoderMs) + "\n"),
            DecoderConnection.EasyLapSerial => DecoderWire.EasyLapHeartbeat(_sequence++, (uint)decoderMs),

            // A Cano sends passings and nothing else.
            _ => null,
        };
    }

    private byte[] Pass(Car car, long elapsedMs)
    {
        var decoderMs = _clockStartMs + elapsedMs;
        var hits = (byte)_random.Next(8, 40);

        return _decoder switch
        {
            DecoderConnection.MyLapsP3 => DecoderWire.P3Passing(++_passingNumber, car.Id,
                _rtcStartMicroseconds + (ulong)elapsedMs * 1000, (ushort)_random.Next(80, 140), hits, 0x405FC),

            // ZRound's race clock starts at zero when HeatCaller says go (%I&).
            DecoderConnection.ZRound => Encoding.ASCII.GetBytes(DecoderWire.ZRoundLap(car.Id, elapsedMs)),
            DecoderConnection.Cano => Encoding.ASCII.GetBytes(DecoderWire.CanoPassing(car.Id, decoderMs, hits, _random.Next(60, 100))),
            DecoderConnection.OpenStint => Encoding.ASCII.GetBytes(DecoderWire.OpenStintPassing(decoderMs, car.Id, hits) + "\n"),
            DecoderConnection.EasyLapSerial => DecoderWire.EasyLapPass(_sequence++, car.Id, (uint)decoderMs, hits, (byte)_random.Next(150, 250)),
            _ => Array.Empty<byte>(),
        };
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    public void Dispose() => Stop();
}
