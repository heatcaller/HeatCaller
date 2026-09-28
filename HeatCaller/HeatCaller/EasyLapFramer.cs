namespace HeatCaller;

/// <summary>
/// The EasyLap byte stream, from HID reports to lap events - everything
/// DirectDecoderClient does with the bytes once it has read them, with no
/// device attached.
/// </summary>
/// <remarks>
/// Split out of DirectDecoderClient's read loop (27 Sep 2026) so a capture
/// of a real night can be played back through exactly the code that read it
/// (EasyLapCaptureTests, from rehearsal 3's log). The logic and the log lines
/// are the ones the read loop had; only where they live has changed.
///
/// One instance per read loop, and not thread-safe: the loop that owns it is
/// the only thing that touches it. A reconnect starts a new loop with a new
/// framer, so an old loop winding down can never mix its bytes into the new
/// one's.
///
/// THE PROTOCOL, as confirmed against USB captures and Next Level Timing's
/// own log (see DirectDecoderClient):
///
///  - Each HID report is [n, then n bytes of the UART stream, then whatever
///    was left in the report buffer]. Only the n bytes are data.
///  - The stream is a run of messages, each starting with its own length:
///    0x0D (13 bytes) is a pass, 0x0B (11 bytes) is a heartbeat. A message
///    can be split across reports.
///  - A pass: transponder in bytes 2-4 (shown as hex, "84C1C4"), the
///    decoder's clock in milliseconds in bytes 7-10 (little-endian), then
///    the hit count and the signal strength.
///  - A heartbeat: the decoder's clock in milliseconds in bytes 3-6
///    (little-endian), once a second. Found in rehearsal 3's capture (27 Sep
///    2026): 738 of them over 737 seconds, each 992-1008ms after the last,
///    on the same clock as the passes. Bytes 7-10 were the same in every
///    one. Used to tell a decoder that has stopped from one with nothing
///    crossing it (TimingWatch).
/// </remarks>
internal sealed class EasyLapFramer
{
    // The only two message lengths the decoder is known to send (see
    // HandleMessage): 0x0D = 13-byte lap pass, 0x0B = 11-byte heartbeat. The
    // framer only trusts a leading byte that is one of these - anything else
    // means we are out of step with the stream, so one byte is dropped and the
    // next position is tried until we resync.
    private const byte LapMessageLength = 0x0D;
    private const byte HeartbeatMessageLength = 0x0B;

    // A healthy stream never holds more than a partial message; if the buffer
    // gets this big something is badly wrong and it is thrown away rather than
    // growing for the rest of the session.
    private const int MaxBufferBytes = 4096;

    private readonly List<byte> _buffer = new();
    private readonly Action<string> _log;
    private readonly Action<DirectLapEvent> _lap;
    private readonly Action<uint>? _heartbeat;

    /// <param name="log">Given each console line, without a time stamp - "[raw HID] ...", "[decoded msg] ...", "[LAP EVENT] ...".</param>
    /// <param name="lap">Given each pass as it is decoded. Anything it throws comes out of AddReport.</param>
    /// <param name="heartbeat">Given the decoder's clock from each heartbeat. Anything it throws comes out of AddReport.</param>
    public EasyLapFramer(Action<string> log, Action<DirectLapEvent> lap, Action<uint>? heartbeat = null)
    {
        _log = log;
        _lap = lap;
        _heartbeat = heartbeat;
    }

    /// <summary>
    /// Takes one HID report as read from the decoder: logs it, adds its data
    /// bytes to the stream, and handles every whole message that completes.
    /// </summary>
    public void AddReport(ReadOnlySpan<byte> report, CancellationToken ct = default)
    {
        if (report.IsEmpty)
        {
            return;
        }

        _log($"[raw HID] {ToHex(report)}");

        var length = report[0];
        if (length > 0 && length < report.Length)
        {
            for (var i = 1; i <= length; i++)
            {
                _buffer.Add(report[i]);
            }
        }

        ProcessBuffer(ct);
    }

    /// <summary>Throws away any partial message - after a report that could not be handled.</summary>
    public void Clear() => _buffer.Clear();

    private void ProcessBuffer(CancellationToken ct)
    {
        if (_buffer.Count > MaxBufferBytes)
        {
            _log($"[framing] Buffer reached {_buffer.Count} bytes without a valid message - discarding it.");
            _buffer.Clear();
            return;
        }

        var skipped = 0;
        while (_buffer.Count > 0 && !ct.IsCancellationRequested)
        {
            var msgLen = _buffer[0];
            if (msgLen != LapMessageLength && msgLen != HeartbeatMessageLength)
            {
                // Not the start of any message we know - we are out of step
                // (a dropped/garbled byte upstream). Slide forward one byte
                // and try again rather than trusting it as a length.
                _buffer.RemoveAt(0);
                skipped++;
                continue;
            }

            if (_buffer.Count < msgLen)
            {
                break;
            }

            var message = _buffer.GetRange(0, msgLen).ToArray();
            _buffer.RemoveRange(0, msgLen);

            HandleMessage(message);
        }

        if (skipped > 0)
        {
            _log($"[framing] Skipped {skipped} byte(s) to resynchronise with the decoder stream.");
        }
    }

    private void HandleMessage(byte[] message)
    {
        _log($"[decoded msg] {ToHex(message)}");

        if (message.Length == 13 && message[0] == 0x0d)
        {
            var transponder = $"{message[2]:X2}{message[3]:X2}{message[4]:X2}";
            var deviceTimeMs = (uint)(message[7] | (message[8] << 8) | (message[9] << 16) | (message[10] << 24));
            var signalHits = message[11];
            var signalStrength = message[12];

            _log($"[LAP EVENT] transponder={transponder}  device_time={deviceTimeMs}ms  " +
                 $"signal_hits={signalHits}  signal_strength={signalStrength}");

            _lap(new DirectLapEvent
            {
                TransponderId = transponder,
                DeviceTimeMs = deviceTimeMs,
                SignalHits = signalHits,
                SignalStrength = signalStrength,
            });
            return;
        }

        if (message.Length == 11 && message[0] == 0x0b)
        {
            // Not logged beyond the [decoded msg] line above - one a second
            // would bury everything else.
            _heartbeat?.Invoke((uint)(message[3] | (message[4] << 8) | (message[5] << 16) | (message[6] << 24)));
            return;
        }

        _log($"[unknown message type] length={message.Length}");
    }

    internal static string ToHex(ReadOnlySpan<byte> bytes)
    {
        var text = new System.Text.StringBuilder(bytes.Length * 3);

        foreach (var b in bytes)
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(b.ToString("x2"));
        }

        return text.ToString();
    }
}
