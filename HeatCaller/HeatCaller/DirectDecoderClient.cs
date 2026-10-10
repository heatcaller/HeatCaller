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

    /// <summary>
    /// When the pass reached the app, on TimingClock - 0 when nobody stamped
    /// it (a ghost, a test). What places a pass from one decoder against a
    /// pass from another; the race itself keeps using DeviceTimeMs.
    /// </summary>
    public long ReceivedMs { get; init; }

    /// <summary>Which decoder saw it (DecoderConnection.EasyLap or ILap), or 0 when none did.</summary>
    public int Decoder { get; init; }

    /// <summary>Where on the track - the line, unless the decoder is a split loop.</summary>
    public TimingPoint Point { get; init; }

    /// <summary>
    /// A copy under another transponder, every other field carried across -
    /// for the race filing an alternate transponder's pass under the main
    /// one. If this class grows a field, copy it here.
    /// </summary>
    public DirectLapEvent WithTransponder(string transponderId) => new()
    {
        TransponderId = transponderId,
        DeviceTimeMs = DeviceTimeMs,
        SignalHits = SignalHits,
        SignalStrength = SignalStrength,
        IsSynthetic = IsSynthetic,
        ReceivedMs = ReceivedMs,
        Decoder = Decoder,
        Point = Point,
    };

    /// <summary>A copy saying which decoder saw it, when it arrived and where it was seen.</summary>
    public DirectLapEvent Stamped(int decoder, long receivedMs, TimingPoint point) => new()
    {
        TransponderId = TransponderId,
        DeviceTimeMs = DeviceTimeMs,
        SignalHits = SignalHits,
        SignalStrength = SignalStrength,
        IsSynthetic = IsSynthetic,
        ReceivedMs = receivedMs,
        Decoder = decoder,
        Point = point,
    };
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

    /// <summary>
    /// 1 while a connect is in flight on a thread of its own.
    ///
    /// The auto-connect retry timer asks every three seconds, so a connect
    /// still inside the driver WILL be asked for again before it is done -
    /// and two of them would open two streams and start two read loops on
    /// one decoder, both framing the same bytes.
    /// </summary>
    private int _connecting;

    /// <summary>
    /// Bumped by every Connect and every Disconnect. A connect that comes
    /// back out of the driver to find this number moved on is working for a
    /// connection nobody wants any more: it closes whatever it opened
    /// rather than publishing it over what happened in the meantime.
    /// </summary>
    private int _generation;

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

    /// <summary>
    /// How this decoder's clock relates to the app's, learned from its
    /// heartbeats and passes as they arrive - see DecoderClockMap. Started
    /// again on every connection.
    /// </summary>
    public DecoderClockMap Clock { get; } = new();

    public DecoderConnectionState State { get; private set; } = DecoderConnectionState.Disconnected;

    /// <summary>
    /// Starts connecting to the decoder, and hands back as soon as that is
    /// under way - with <see cref="State"/> already Connecting.
    ///
    /// All of this used to run on the caller's thread, and the caller is the
    /// UI thread: the auto-connect retry timer is a
    /// System.Windows.Forms.Timer ticking every three seconds. Nothing in
    /// the sequence has a timeout - the old stream's Dispose waits for the
    /// read loop to come out of stream.Read, GetHidDevices is a SetupAPI
    /// enumeration, TryOpen is a CreateFile, and the two feature reports are
    /// driver IOCTLs - so a CP2110 wedged on the bus with its device node
    /// still present froze the window with no exception and almost no CPU.
    /// That is the ten-second freeze rehearsal 2 saw, and the reason
    /// HangWatchdog exists. The READS were moved off this thread then (see
    /// ReadTimeout in ReadLoop); the connect was left behind. It is off it
    /// now, and the result is marshalled back the same way the read loop's
    /// is - through SetState and the status event.
    ///
    /// What callers see is unchanged: they set the buttons around this and
    /// then read State, and State is Connecting the instant this returns.
    /// That is also what stops the retry timer asking again mid-connect - it
    /// acts only on Disconnected or Error.
    /// </summary>
    public void Connect()
    {
        if (Interlocked.Exchange(ref _connecting, 1) == 1)
        {
            // One at a time. Nothing is said about it: the retry timer asks
            // every three seconds and would fill the console with it.
            return;
        }

        var generation = Interlocked.Increment(ref _generation);

        SetState(DecoderConnectionState.Connecting, "Looking for the EasyLap decoder (Silicon Labs CP2110)...");

        _ = Task.Run(() => ConnectCore(generation));
    }

    /// <summary>
    /// The connect itself, on a pool thread: tear the old one down, find the
    /// decoder, open it, configure the UART, start the read loop. Everything
    /// in here is allowed to block for as long as the driver likes.
    /// </summary>
    /// <param name="generation">
    /// This attempt's number - see <see cref="_generation"/>. Once it has
    /// moved on, this attempt says nothing and keeps nothing.
    /// </param>
    private void ConnectCore(int generation)
    {
        void Report(DecoderConnectionState state, string message)
        {
            // Nothing is published on behalf of an attempt that has been
            // overtaken: the Disconnect that overtook it has had the last
            // word, and an Error arriving after it would turn the status
            // light red for a decoder nobody is trying to open.
            if (Volatile.Read(ref _generation) == generation)
            {
                SetState(state, message);
            }
        }

        try
        {
            // The old connection goes first, off the UI thread with the rest
            // of it. State is deliberately left alone here - Connecting has
            // already been published and this is still the same attempt, not
            // a disconnection.
            Teardown();

            // A new connection may be a different decoder, or the same one
            // restarted: its clock is learned again from scratch.
            Clock.Reset();

            HidDevice? device;
            try
            {
                device = DeviceList.Local.GetHidDevices(VendorId, ProductId).FirstOrDefault();
            }
            catch (Exception ex)
            {
                Report(DecoderConnectionState.Error, $"Failed to enumerate HID devices: {ex.Message}");
                return;
            }

            if (device is null)
            {
                Report(DecoderConnectionState.Error,
                    "No CP2110 device found (VID 0x10C4, PID 0x86B9). Is the EasyLap decoder plugged in?");
                return;
            }

            Log($"Found device: {SafeDescribe(device)}");

            // Into a local, not straight into _stream: until this attempt
            // has been checked for still being the current one, the stream
            // it opened is nobody's.
            HidStream? stream;

            if (!device.TryOpen(out stream) || stream is null)
            {
                Report(DecoderConnectionState.Error,
                    "Found the decoder but could not open it. If Next Level Timing (or another instance of this app) " +
                    "already has it open, only one application can use a HID device at a time - close the other one first.");
                return;
            }

            try
            {
                stream.SetFeature(UartEnableReport);
                Log($"Sent UART Enable report: {ToHex(UartEnableReport)}");

                stream.SetFeature(UartConfigReport);
                Log($"Sent UART Config report (38400 8N1): {ToHex(UartConfigReport)}");
            }
            catch (Exception ex)
            {
                Report(DecoderConnectionState.Error, $"Failed to configure the UART: {ex.Message}");
                CloseQuietly(stream);
                return;
            }

            var maxInputReportLength = device.GetMaxInputReportLength();

            // The last look before this stream becomes the live one. A
            // Disconnect - or a later Connect - while the driver had us
            // means this decoder was opened for nobody, so it is closed
            // again rather than published over whatever is there now. This
            // is the check that stops two read loops running on one
            // decoder.
            if (Volatile.Read(ref _generation) != generation)
            {
                CloseQuietly(stream);
                return;
            }

            var cts = new CancellationTokenSource();
            var token = cts.Token;

            _stream = stream;
            _cts = cts;

            // Looked at once more now the stream IS the live one: a
            // Disconnect landing between the look above and this assignment
            // would have found nothing to close, and the read loop would
            // have started on a decoder nobody wanted.
            if (Volatile.Read(ref _generation) != generation)
            {
                Teardown();
                return;
            }

            SetState(DecoderConnectionState.Connected, "Connected. Listening for lap events.");

            _ = Task.Run(() => ReadLoop(stream, maxInputReportLength, token), token);
        }
        catch (Exception ex)
        {
            // Nobody waits on this task, so anything escaping here would be
            // an unobserved exception on a pool thread - which takes the app
            // down later, from the finalizer, with nothing to say why.
            try
            {
                Report(DecoderConnectionState.Error, $"Connecting to the decoder failed: {ex.Message}");
            }
            catch
            {
                // A subscriber threw while reporting the failure - nothing more we can do here.
            }
        }
        finally
        {
            // Cleared last, so the retry timer's next tick may try again. A
            // driver call that NEVER returns leaves this set and no further
            // connect is attempted - which is the right answer: the window
            // is free either way, and asking a wedged driver again only
            // wedges another thread behind the first.
            Volatile.Write(ref _connecting, 0);
        }
    }

    /// <summary>Closes a stream a connect opened and is not going to use. Never throws.</summary>
    private static void CloseQuietly(HidStream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch
        {
            // ignore - nothing is listening on it
        }
    }

    private void ReadLoop(HidStream stream, int maxInputReportLength, CancellationToken ct)
    {
        // The framer (and its buffer) is local to this loop on purpose: a
        // reconnect starts a fresh loop with its own, so an old loop that is
        // still winding down can never interleave bytes with the new one, and
        // Disconnect() (UI thread) never has to touch it while it is in use.
        // Each pass and heartbeat is stamped with when it arrived (on the
        // app's clock) the moment it is framed, on this thread - and both
        // teach the clock map: a heartbeat carries the decoder's clock, and
        // so does every pass.
        var framer = new EasyLapFramer(
            Log,
            lap =>
            {
                var arrived = TimingClock.NowMs;
                Clock.Add(arrived, lap.DeviceTimeMs);
                LapEventReceived?.Invoke(lap.Stamped(DecoderConnection.EasyLap, arrived, TimingPoint.Line));
            },
            clock =>
            {
                Clock.Add(TimingClock.NowMs, clock);
                HeartbeatReceived?.Invoke(clock);
            });

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
        // Any connect still inside the driver is now working for a
        // connection nobody wants - see ConnectCore.
        Interlocked.Increment(ref _generation);

        Teardown();

        if (State != DecoderConnectionState.Disconnected)
        {
            SetState(DecoderConnectionState.Disconnected, "Disconnected.");
        }
    }

    /// <summary>
    /// Stops the read loop and lets the stream go, without touching
    /// <see cref="State"/>. Its own method because a connect does this too,
    /// on its own thread, and must not publish Disconnected in the middle of
    /// connecting.
    /// </summary>
    private void Teardown()
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
