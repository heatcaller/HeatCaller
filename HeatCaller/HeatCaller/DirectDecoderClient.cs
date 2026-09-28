using HidSharp;

namespace HeatCaller;

public enum DecoderConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}

/// <summary>A single decoded lap-pass event from the raw EasyLap serial protocol.</summary>
public sealed class DirectLapEvent
{
    public required string TransponderId { get; init; }
    public uint DeviceTimeMs { get; init; }
    public int SignalHits { get; init; }
    public int SignalStrength { get; init; }

    /// <summary>
    /// True for a pass generated inside the app (a ghost car) rather than
    /// read from the decoder. For these, DeviceTimeMs is milliseconds since
    /// the green flag, not a decoder clock reading - and they never move
    /// the session's decoder clock estimate.
    /// </summary>
    public bool IsSynthetic { get; init; }
}

/// <summary>
/// Talks directly to the EasyLap / Robitronic USB decoder's Silicon Labs
/// CP2110 HID-to-UART bridge chip, bypassing Next Level Timing entirely.
/// See the standalone EasyLapDirectClient proof-of-concept project's README
/// for the full reverse-engineering writeup this is based on - confirmed
/// against real USB captures and cross-checked field-by-field against
/// Next Level Timing's own debug log output for the same physical laps.
///
/// A later, dedicated capture also confirmed NLT sends no other command to
/// the decoder beyond standard USB enumeration and the two Feature reports
/// below (UART Enable + Config) - there is no separate "reset" command
/// this client is missing. Full protocol parity with NLT's own connection
/// sequence is confirmed, not assumed.
/// </summary>
public sealed class DirectDecoderClient : IDisposable
{
    private const int VendorId = 0x10C4;
    private const int ProductId = 0x86B9;

    private static readonly byte[] UartEnableReport = { 0x41, 0x01 };
    private static readonly byte[] UartConfigReport =
    {
        0x50,                   // Report ID
        0x00, 0x00, 0x96, 0x00, // Baud rate, big-endian = 38400
        0x00,                   // Parity: none
        0x00,                   // Flow control: none
        0x03,                   // Data bits: 8
        0x00,                   // Stop bits: 1
    };

    private HidStream? _stream;
    private CancellationTokenSource? _cts;

    // What is done with the bytes once they are read - framing, decoding,
    // the console lines - is EasyLapFramer, so a captured night can be
    // played back through it with no decoder attached.

    public event Action<DecoderConnectionState, string>? StatusChanged;
    public event Action<string>? RawMessage;
    public event Action<DirectLapEvent>? LapEventReceived;

    /// <summary>
    /// Raised for each heartbeat, about once a second, with the decoder's
    /// clock in milliseconds - see EasyLapFramer. On the decoder's own
    /// thread.
    /// </summary>
    public event Action<uint>? HeartbeatReceived;

    public DecoderConnectionState State { get; private set; } = DecoderConnectionState.Disconnected;

    public void Connect()
    {
        Disconnect();

        SetState(DecoderConnectionState.Connecting, "Looking for the EasyLap decoder (Silicon Labs CP2110)...");

        HidDevice? device;
        try
        {
            device = DeviceList.Local.GetHidDevices(VendorId, ProductId).FirstOrDefault();
        }
        catch (Exception ex)
        {
            SetState(DecoderConnectionState.Error, $"Failed to enumerate HID devices: {ex.Message}");
            return;
        }

        if (device is null)
        {
            SetState(DecoderConnectionState.Error,
                "No CP2110 device found (VID 0x10C4, PID 0x86B9). Is the EasyLap decoder plugged in?");
            return;
        }

        Log($"Found device: {SafeDescribe(device)}");

        if (!device.TryOpen(out _stream) || _stream is null)
        {
            SetState(DecoderConnectionState.Error,
                "Found the decoder but could not open it. If Next Level Timing (or another instance of this app) " +
                "already has it open, only one application can use a HID device at a time - close the other one first.");
            return;
        }

        try
        {
            _stream.SetFeature(UartEnableReport);
            Log($"Sent UART Enable report: {ToHex(UartEnableReport)}");

            _stream.SetFeature(UartConfigReport);
            Log($"Sent UART Config report (38400 8N1): {ToHex(UartConfigReport)}");
        }
        catch (Exception ex)
        {
            SetState(DecoderConnectionState.Error, $"Failed to configure the UART: {ex.Message}");
            _stream.Dispose();
            _stream = null;
            return;
        }

        SetState(DecoderConnectionState.Connected, "Connected. Listening for lap events.");

        var maxInputReportLength = device.GetMaxInputReportLength();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var stream = _stream!;
        _ = Task.Run(() => ReadLoop(stream, maxInputReportLength, token), token);
    }

    private void ReadLoop(HidStream stream, int maxInputReportLength, CancellationToken ct)
    {
        // The framer (and its buffer) is local to this loop on purpose: a
        // reconnect starts a fresh loop with its own, so an old loop that is
        // still winding down can never interleave bytes with the new one, and
        // Disconnect() (UI thread) never has to touch it while it is in use.
        var framer = new EasyLapFramer(Log, lap => LapEventReceived?.Invoke(lap), clock => HeartbeatReceived?.Invoke(clock));

        try
        {
            var readBuffer = new byte[Math.Max(maxInputReportLength, 8)];

            try
            {
                stream.ReadTimeout = 1000;
            }
            catch
            {
                // Not all platforms/streams support timeouts - fine, Read() will just block.
            }

            while (!ct.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        SetState(DecoderConnectionState.Error, $"Read failed: {ex.Message}");
                    }
                    return;
                }

                if (bytesRead <= 0)
                {
                    continue;
                }

                // A bad report or a throwing LapEventReceived subscriber must
                // not end the loop - it would die silently while the status
                // still said Connected. Drop whatever was buffered (it is the
                // suspect data) and carry on with the next report.
                try
                {
                    framer.AddReport(readBuffer.AsSpan(0, bytesRead), ct);
                }
                catch (Exception ex)
                {
                    framer.Clear();
                    Log($"[error] Failed to process a decoder report, buffered bytes discarded: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // Anything escaping the per-iteration handling is unrecoverable
            // for this loop. Flag Error (unless we were being torn down anyway)
            // so the auto-reconnect watchdog, which only acts on Disconnected
            // or Error, can bring the decoder back.
            if (!ct.IsCancellationRequested)
            {
                try
                {
                    SetState(DecoderConnectionState.Error, $"Decoder read loop stopped: {ex.Message}");
                }
                catch
                {
                    // A subscriber threw while reporting the failure - nothing more we can do here.
                }
            }
        }
    }

    public void Disconnect()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            // Safe to dispose straight away: the read loop only polls
            // ct.IsCancellationRequested, which keeps working on a disposed
            // source, and never registers callbacks on the token.
            _cts.Dispose();
            _cts = null;
        }

        if (_stream is not null)
        {
            try
            {
                _stream.Dispose();
            }
            catch
            {
                // ignore - tearing down anyway
            }
            _stream = null;
        }

        if (State != DecoderConnectionState.Disconnected)
        {
            SetState(DecoderConnectionState.Disconnected, "Disconnected.");
        }
    }

    private void SetState(DecoderConnectionState state, string message)
    {
        State = state;
        StatusChanged?.Invoke(state, message);
        Log($"[status] {message}");
    }

    private void Log(string line) => RawMessage?.Invoke($"{AppTime.LogStampNow()}  {line}");

    private static string ToHex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("x2")));

    private static string SafeDescribe(HidDevice device)
    {
        try
        {
            return device.GetFriendlyName();
        }
        catch
        {
            return $"VID_{device.VendorID:X4}&PID_{device.ProductID:X4}";
        }
    }

    public void Dispose() => Disconnect();
}
