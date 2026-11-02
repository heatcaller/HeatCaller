using System.Globalization;
using System.Text;

namespace HeatCaller;

/// <summary>
/// One thing a decoder said that matters to timing: a car crossing the loop,
/// or only a reading of the decoder's own clock (a heartbeat, a status
/// record).
/// </summary>
/// <param name="TransponderId">The car, as HeatCaller shows it - null for a clock reading alone.</param>
/// <param name="DeviceMs">The decoder's clock when it happened, in milliseconds - null when the record carries none.</param>
/// <param name="Hits">How many times the loop read the transponder, 0 when not known.</param>
/// <param name="Strength">The decoder's signal figure, 0 when not known.</param>
internal readonly record struct DecoderReading(string? TransponderId, long? DeviceMs, int Hits = 0, int Strength = 0);

/// <summary>
/// How a decoder from 3 up is reached. Saved as its number in settings.json
/// (StreamDecoderSettings), so add to the end and never reorder.
/// </summary>
public enum DecoderTransport
{
    /// <summary>A COM port - USB serial, Bluetooth or a real one.</summary>
    Serial,

    /// <summary>The decoder's IP address and port: TCP, or ZeroMQ for OpenStint.</summary>
    Network,

    /// <summary>A decoder made up inside HeatCaller, speaking the real protocol, for rehearsing at home.</summary>
    Simulated,
}

/// <summary>
/// One decoder's wire format, from bytes to <see cref="DecoderReading"/>s.
/// One instance per connection and not thread-safe: the reader that owns it
/// is the only thing that touches it.
/// </summary>
/// <remarks>
/// WHY (1 Nov 2026): the decoders added from the support plan
/// (plans/decoder-support-plan.md) each differ only here. Everything around
/// the bytes - the port or socket, reconnecting, the raw capture, the clock -
/// is StreamDecoderClient's and StreamDecoderReader's, shared by all of them.
/// Every format is written from its published description, not from anybody's
/// code, and each is tested against records built from those descriptions.
/// </remarks>
internal interface IDecoderProtocol
{
    /// <summary>True when the decoder talks in text, so the raw capture keeps it as text rather than hex.</summary>
    bool IsText { get; }

    /// <summary>What to send the decoder as soon as it is connected - empty for most.</summary>
    byte[] Greeting { get; }

    /// <summary>Takes bytes as they arrived and adds whatever they completed to <paramref name="readings"/>.</summary>
    void Feed(ReadOnlySpan<byte> bytes, List<DecoderReading> readings, Action<string> log);
}

/// <summary>Which protocol, transports and defaults go with each decoder from 3 up.</summary>
internal static class DecoderProtocols
{
    /// <summary>A new protocol for <paramref name="decoder"/> - one per connection.</summary>
    internal static IDecoderProtocol For(int decoder) => decoder switch
    {
        DecoderConnection.MyLapsP3 => new P3Protocol(),
        DecoderConnection.ZRound => new ZRoundProtocol(),
        DecoderConnection.Cano => new CanoProtocol(),
        DecoderConnection.OpenStint => new OpenStintProtocol(),
        DecoderConnection.EasyLapSerial => new EasyLapStreamProtocol(),
        _ => throw new ArgumentOutOfRangeException(nameof(decoder), decoder, "Not a decoder StreamDecoderClient runs."),
    };

    /// <summary>The ways <paramref name="decoder"/> can be reached, the usual one first.</summary>
    internal static DecoderTransport[] Transports(int decoder) => decoder switch
    {
        // P3 is spoken over the network by AMBrc, RC4 and the rest; serial
        // P3 exists on some, so it is offered.
        DecoderConnection.MyLapsP3 => new[] { DecoderTransport.Network, DecoderTransport.Serial, DecoderTransport.Simulated },

        // The open protocol's own page names both, and a RiCino is usually on USB serial.
        DecoderConnection.ZRound => new[] { DecoderTransport.Serial, DecoderTransport.Network, DecoderTransport.Simulated },

        // ZeroMQ only - OpenStint publishes nothing else.
        DecoderConnection.OpenStint => new[] { DecoderTransport.Network, DecoderTransport.Simulated },

        _ => new[] { DecoderTransport.Serial, DecoderTransport.Simulated },
    };

    /// <summary>The network port the decoder listens on unless set otherwise.</summary>
    internal static int DefaultPort(int decoder) => decoder switch
    {
        // P3: the decoder serves up to four connections on 5403.
        DecoderConnection.MyLapsP3 => 5403,

        // OpenStint's bridge to ZRound listens on 5001 by default.
        DecoderConnection.ZRound => 5001,

        // OpenStint's ZeroMQ publisher.
        DecoderConnection.OpenStint => 5556,
        _ => 0,
    };

    /// <summary>The serial rate the decoder uses unless set otherwise.</summary>
    internal static int DefaultBaud(int decoder) => decoder switch
    {
        // The open protocol's page: 19200 8N1.
        DecoderConnection.ZRound => 19200,

        // RCHourglass's protocol page: 57600 8N1 on its serial port.
        DecoderConnection.Cano => 57600,

        // The UART inside the USB EasyLap.
        DecoderConnection.EasyLapSerial => 38400,

        // Serial P3's rate is not published; the decoder's own setting wins.
        _ => 19200,
    };

    /// <summary>Little-endian unsigned number from <paramref name="length"/> bytes (1 to 8).</summary>
    internal static ulong ReadLittleEndian(IReadOnlyList<byte> bytes, int offset, int length)
    {
        ulong value = 0;

        for (var i = length - 1; i >= 0; i--)
        {
            value = (value << 8) | bytes[offset + i];
        }

        return value;
    }

    /// <summary>True when every character is a hex digit, and there is at least one.</summary>
    internal static bool IsHex(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    internal static ulong ParseHex(ReadOnlySpan<char> text) =>
        ulong.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
}

/// <summary>
/// MyLaps / AMB P3: AMBrc, RC4, and per ammc also TranX3, ChipX and BIB.
/// </summary>
/// <remarks>
/// <para>
/// A record is 0x8E, a header, fields, then 0x8F. Inside it, 0x8D escapes
/// the next byte, which is sent 0x20 higher - so 0x8D, 0x8E and 0x8F never
/// appear in the middle of a record. The header, after the 0x8E: version (1
/// byte), the record's length unescaped from 0x8E to 0x8F (2 bytes), a CRC
/// (2), flags (2) and the record type (2), all little-endian. Then fields to
/// the end: an id byte, a length byte, and that many bytes of value.
/// </para>
/// <para>
/// Type 1 is a PASSING: 0x01 passing number, 0x03 transponder, 0x04 the
/// decoder's clock in microseconds (8 bytes), 0x05 signal strength, 0x06
/// hits, 0x08 flags, 0x81 the decoder's id. Type 2 is a STATUS (noise,
/// temperature, input voltage) and carries no clock. Everything else is
/// ignored.
/// </para>
/// <para>
/// The CRC is not checked. Its algorithm isn't published, and none of six
/// common CRC-16s matched the sample records, so a record is read whatever
/// its CRC says rather than every one thrown away. A record whose length
/// disagrees with its header is reported and still read, as long as its
/// fields fit.
/// </para>
/// </remarks>
internal sealed class P3Protocol : IDecoderProtocol
{
    private const byte Start = 0x8E;
    private const byte End = 0x8F;
    private const byte Escape = 0x8D;
    private const int HeaderLength = 10;

    /// <summary>Far longer than any record a decoder sends - one this long is a lost end marker.</summary>
    private const int MaxRecordLength = 1024;

    private readonly List<byte> _record = new();
    private bool _inRecord;
    private bool _escaped;
    private readonly HashSet<int> _typesSeen = new();

    public bool IsText => false;

    public byte[] Greeting => Array.Empty<byte>();

    public void Feed(ReadOnlySpan<byte> bytes, List<DecoderReading> readings, Action<string> log)
    {
        foreach (var b in bytes)
        {
            if (b == Start)
            {
                if (_inRecord && _record.Count > 1)
                {
                    log($"[p3] A record was cut short after {_record.Count} bytes - dropped.");
                }

                _record.Clear();
                _record.Add(Start);
                _inRecord = true;
                _escaped = false;
                continue;
            }

            if (!_inRecord)
            {
                continue;
            }

            if (_escaped)
            {
                _record.Add((byte)(b - 0x20));
                _escaped = false;
            }
            else if (b == Escape)
            {
                _escaped = true;
            }
            else if (b == End)
            {
                _record.Add(End);
                _inRecord = false;
                Handle(_record, readings, log);
                _record.Clear();
                continue;
            }
            else
            {
                _record.Add(b);
            }

            if (_record.Count > MaxRecordLength)
            {
                log($"[p3] {_record.Count} bytes with no end marker - dropped. Check this is a P3 decoder.");
                _record.Clear();
                _inRecord = false;
            }
        }
    }

    /// <summary>One whole, unescaped record, 0x8E to 0x8F.</summary>
    private void Handle(List<byte> record, List<DecoderReading> readings, Action<string> log)
    {
        if (record.Count < HeaderLength + 1)
        {
            log($"[p3] A record of {record.Count} bytes is too short to read - dropped.");
            return;
        }

        var length = (int)DecoderProtocols.ReadLittleEndian(record, 2, 2);
        var type = (int)DecoderProtocols.ReadLittleEndian(record, 8, 2);

        if (length != record.Count)
        {
            log($"[p3] A record's header says {length} bytes but it had {record.Count} - read anyway.");
        }

        if (type is not (1 or 2) && _typesSeen.Add(type))
        {
            log($"[p3] The decoder sent a record of type 0x{type:X2} - not needed for timing, ignored from now on.");
        }

        if (type != 1)
        {
            return;
        }

        ulong? transponder = null;
        ulong? rtcMicroseconds = null;
        var strength = 0;
        var hits = 0;

        // Fields run to the byte before the end marker.
        var at = HeaderLength;
        var end = record.Count - 1;

        while (at + 2 <= end)
        {
            var id = record[at];
            var size = record[at + 1];
            var value = at + 2;

            if (value + size > end)
            {
                log($"[p3] A passing's field 0x{id:X2} runs past the end of the record - the rest of it is not read.");
                break;
            }

            if (size is >= 1 and <= 8)
            {
                var number = DecoderProtocols.ReadLittleEndian(record, value, size);

                switch (id)
                {
                    case 0x03:
                        transponder = number;
                        break;
                    case 0x04:
                        rtcMicroseconds = number;
                        break;
                    case 0x05:
                        strength = (int)Math.Min(number, int.MaxValue);
                        break;
                    case 0x06:
                        hits = (int)Math.Min(number, int.MaxValue);
                        break;
                }
            }

            at = value + size;
        }

        if (transponder is not { } car)
        {
            log("[p3] A passing with no transponder in it - dropped.");
            return;
        }

        readings.Add(new DecoderReading(
            car.ToString(CultureInfo.InvariantCulture),
            rtcMicroseconds is { } us ? (long)(us / 1000) : null,
            hits,
            strength));
    }
}

/// <summary>
/// The ZRound open protocol: RiCino, anything else ZRound lists as using it,
/// and OpenStint through its ZRound bridge.
/// </summary>
/// <remarks>
/// Every message is '%' ... '&amp;'. HeatCaller sends %C&amp; (the decoder
/// answers %A&amp;) and %I&amp; (race start: the decoder sets its clock to
/// zero and starts reporting - OpenStint's bridge sends nothing until it).
/// The decoder sends %T then its clock in hex milliseconds, and
/// %L then the transponder in hex, then the race time in hex milliseconds:
/// with a comma between them the transponder may be any length, without one
/// it is two hex digits. %F&amp; (race stop) is never sent: HeatCaller
/// decides when a race ends, and the decoder carries on reporting.
/// </remarks>
internal sealed class ZRoundProtocol : IDecoderProtocol
{
    /// <summary>Far longer than any message - one this long lost its '&amp;'.</summary>
    private const int MaxMessageLength = 64;

    private readonly StringBuilder _message = new();
    private bool _inMessage;
    private bool _acknowledged;

    public bool IsText => true;

    public byte[] Greeting => Encoding.ASCII.GetBytes("%C&%I&");

    public void Feed(ReadOnlySpan<byte> bytes, List<DecoderReading> readings, Action<string> log)
    {
        foreach (var b in bytes)
        {
            var c = (char)b;

            if (c == '%')
            {
                _message.Clear();
                _inMessage = true;
            }
            else if (!_inMessage)
            {
                continue;
            }
            else if (c == '&')
            {
                _inMessage = false;
                Handle(_message.ToString().Trim(), readings, log);
                _message.Clear();
            }
            else if (_message.Length >= MaxMessageLength)
            {
                log($"[zround] {_message.Length} characters with no '&' - dropped. Check the baud rate.");
                _message.Clear();
                _inMessage = false;
            }
            else
            {
                _message.Append(c);
            }
        }
    }

    private void Handle(string body, List<DecoderReading> readings, Action<string> log)
    {
        if (body.Length == 0)
        {
            return;
        }

        var rest = body.AsSpan(1).Trim();

        switch (char.ToUpperInvariant(body[0]))
        {
            case 'A':
                if (!_acknowledged)
                {
                    _acknowledged = true;
                    log("[zround] The decoder answered the handshake.");
                }

                return;

            case 'T':
                if (DecoderProtocols.IsHex(rest) && rest.Length <= 15)
                {
                    readings.Add(new DecoderReading(null, (long)DecoderProtocols.ParseHex(rest)));
                    return;
                }

                break;

            case 'L':
                if (ReadLap(rest) is { } lap)
                {
                    readings.Add(lap);
                    return;
                }

                break;
        }

        log($"[zround] Not a message HeatCaller reads: %{body}&");
    }

    /// <summary>The transponder and time of a %L message, after the L - or null when it is not one.</summary>
    internal static DecoderReading? ReadLap(ReadOnlySpan<char> rest)
    {
        ReadOnlySpan<char> car;
        ReadOnlySpan<char> time;
        var comma = rest.IndexOf(',');

        if (comma >= 0)
        {
            car = rest[..comma].Trim();
            time = rest[(comma + 1)..].Trim();
        }
        else if (rest.Length > 2)
        {
            car = rest[..2];
            time = rest[2..];
        }
        else
        {
            return null;
        }

        if (!DecoderProtocols.IsHex(car) || car.Length > 15 || !DecoderProtocols.IsHex(time) || time.Length > 15)
        {
            return null;
        }

        return new DecoderReading(
            DecoderProtocols.ParseHex(car).ToString(CultureInfo.InvariantCulture),
            (long)DecoderProtocols.ParseHex(time));
    }
}

/// <summary>Howard Cano's DIY decoder, and RCHourglass in Cano or RCHourglass mode.</summary>
/// <remarks>
/// One ASCII line per passing, ending CR LF: six hex digits of transponder
/// and eight of time, in quarter milliseconds since the decoder powered up -
/// "23E35500075A06" is transponder 2351957 at 120.449 s. RCHourglass mode
/// adds "-" and five two-digit hex fields: decoder id, hits, read quality
/// (0-100 %), voltage and temperature. The Cano's power-up line ("COPYRIGHT
/// ...") and anything else are not passings.
/// </remarks>
internal sealed class CanoProtocol : IDecoderProtocol
{
    private const int MaxLineLength = 128;

    private readonly StringBuilder _line = new();
    private bool _saidBanner;

    public bool IsText => true;

    public byte[] Greeting => Array.Empty<byte>();

    public void Feed(ReadOnlySpan<byte> bytes, List<DecoderReading> readings, Action<string> log)
    {
        foreach (var b in bytes)
        {
            var c = (char)b;

            if (c is '\r' or '\n')
            {
                if (_line.Length > 0)
                {
                    Handle(_line.ToString().Trim(), readings, log);
                    _line.Clear();
                }
            }
            else if (_line.Length >= MaxLineLength)
            {
                log($"[cano] {_line.Length} characters with no line ending - dropped. Check the baud rate.");
                _line.Clear();
            }
            else
            {
                _line.Append(c);
            }
        }
    }

    private void Handle(string line, List<DecoderReading> readings, Action<string> log)
    {
        if (ReadPassing(line) is { } passing)
        {
            readings.Add(passing);
            return;
        }

        if (line.StartsWith("COPYRIGHT", StringComparison.OrdinalIgnoreCase))
        {
            if (!_saidBanner)
            {
                _saidBanner = true;
                log($"[cano] The decoder has just started: {line}");
            }

            return;
        }

        log($"[cano] Not a passing record: {line}");
    }

    /// <summary>One line's passing, or null when the line is not one.</summary>
    internal static DecoderReading? ReadPassing(string line)
    {
        var text = line.AsSpan().Trim();

        if (text.Length < 14 || !DecoderProtocols.IsHex(text[..14]))
        {
            return null;
        }

        // Only a plain Cano record, or one with RCHourglass's fields after a
        // dash - fourteen hex digits followed by anything else is not one.
        if (text.Length > 14 && text[14] != '-')
        {
            return null;
        }

        var car = DecoderProtocols.ParseHex(text[..6]);
        var quarterMs = DecoderProtocols.ParseHex(text[6..14]);
        var hits = 0;
        var quality = 0;

        if (text.Length >= 21 && text[14] == '-' && DecoderProtocols.IsHex(text[15..21]))
        {
            hits = (int)DecoderProtocols.ParseHex(text[17..19]);
            quality = (int)DecoderProtocols.ParseHex(text[19..21]);
        }

        return new DecoderReading(car.ToString(CultureInfo.InvariantCulture), (long)(quarterMs / 4), hits, quality);
    }
}

/// <summary>OpenStint, read natively over ZeroMQ.</summary>
/// <remarks>
/// One text message per ZeroMQ message, fields split by spaces and read by
/// position, more fields possibly added on the end in future:
/// "P time type transponder rssi hits duration ..." is a passing, "S time
/// ..." a status once a second, "T time ..." a transponder's time sync, and
/// "L ..." the RC4 trainer. Every time is the decoder's own millisecond clock
/// since it started. StreamDecoderClient hands each message over with a line
/// ending after it, so a message is a line here.
/// </remarks>
internal sealed class OpenStintProtocol : IDecoderProtocol
{
    private const int MaxLineLength = 512;

    private readonly StringBuilder _line = new();

    public bool IsText => true;

    public byte[] Greeting => Array.Empty<byte>();

    public void Feed(ReadOnlySpan<byte> bytes, List<DecoderReading> readings, Action<string> log)
    {
        foreach (var b in bytes)
        {
            var c = (char)b;

            if (c is '\r' or '\n')
            {
                if (_line.Length > 0)
                {
                    Handle(_line.ToString(), readings, log);
                    _line.Clear();
                }
            }
            else if (_line.Length >= MaxLineLength)
            {
                _line.Clear();
            }
            else
            {
                _line.Append(c);
            }
        }
    }

    private static void Handle(string message, List<DecoderReading> readings, Action<string> log)
    {
        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var time))
        {
            return;
        }

        switch (parts[0])
        {
            case "P":
                if (parts.Length < 4 || !uint.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var car))
                {
                    log($"[openstint] A passing HeatCaller can't read: {message}");
                    return;
                }

                var hits = parts.Length > 5 && int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ? h : 0;

                // The RSSI is decibels below full scale, nothing like the
                // other decoders' strength figure, so it is left out.
                readings.Add(new DecoderReading(car.ToString(CultureInfo.InvariantCulture), time, hits));
                return;

            case "S":
            case "T":
                readings.Add(new DecoderReading(null, time));
                return;

            case "L":
                log($"[openstint] RC4 trainer: {message}");
                return;
        }
    }
}

/// <summary>
/// An EasyLap or Robitronic on a serial port: the same bytes as the USB
/// EasyLap's UART stream, through the same EasyLapFramer.
/// </summary>
internal sealed class EasyLapStreamProtocol : IDecoderProtocol
{
    private readonly EasyLapFramer _framer;
    private List<DecoderReading>? _into;

    public EasyLapStreamProtocol()
    {
        _framer = new EasyLapFramer(
            Log,
            lap => _into?.Add(new DecoderReading(lap.TransponderId, lap.DeviceTimeMs, lap.SignalHits, lap.SignalStrength)),
            clock => _into?.Add(new DecoderReading(null, clock)));
    }

    private Action<string>? _log;

    private void Log(string line) => _log?.Invoke(line);

    public bool IsText => false;

    public byte[] Greeting => Array.Empty<byte>();

    public void Feed(ReadOnlySpan<byte> bytes, List<DecoderReading> readings, Action<string> log)
    {
        _into = readings;
        _log = log;

        try
        {
            _framer.AddBytes(bytes);
        }
        finally
        {
            _into = null;
            _log = null;
        }
    }
}
