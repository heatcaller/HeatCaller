using System.IO.Ports;
using System.Text;

namespace HeatCaller;

/// <summary>
/// Talks to an I-Lap timing decoder over its USB serial port.
///
/// Built BEFORE the hardware was on the bench, deliberately: the public
/// record on I-Lap's exact wire format is thin, so this client is written
/// the way you'd approach an unlabelled serial device - a tolerant line
/// parser that accepts every passing shape the community's integrations
/// suggest, and a raw capture file that records every byte the decoder
/// actually sends. Day one with the real unit is a ten-minute read of
/// that capture and, at most, one tightening pass on ParseLine - not a
/// guessing game at the track.
///
/// Everything downstream is shared with the EasyLap path: passes come out
/// as the same DirectLapEvent, into the same MainForm handler, the same
/// transponder watch and the same race session.
/// </summary>
public sealed class ILapClient : IDisposable
{
    /// <summary>Common decoder rates to offer; 9600 first because that is the widely-reported I-Lap default.</summary>
    public static readonly int[] CommonBaudRates = { 9600, 19200, 38400, 57600, 115200 };

    private SerialPort? _port;
    private readonly StringBuilder _lineBuffer = new();
    // DeviceTimeMs is the app's own clock (TimingClock) at receipt: it
    // never restarts - a reconnect can't make it jump backwards mid-race
    // and turn every later lap negative - and it is the clock an EasyLap's
    // passes are mapped onto (DecoderClockMap), so an I-Lap split loop and
    // an EasyLap line can be placed against each other.
    private readonly Dictionary<string, long> _lastPassMs = new(StringComparer.Ordinal);
    private StreamWriter? _rawCapture;
    private readonly object _captureLock = new();
    private long _captureBytes; // guarded by _captureLock

    /// <summary>
    /// A decoder that streams garbage (wrong baud rate, a noisy line) would
    /// otherwise fill the disk one day-file at a time. 20 MB is days of real
    /// passing records, so hitting it means something is wrong - capture
    /// stops for this connection with one log line and timing carries on.
    /// </summary>
    private const long MaxCaptureBytesPerDay = 20L * 1024 * 1024;

    /// <summary>Capture files older than this are deleted on connect - they have served their purpose.</summary>
    private static readonly TimeSpan CaptureRetention = TimeSpan.FromDays(14);

    /// <summary>
    /// A line longer than this is not a passing record - it is a device
    /// that never sends a terminator (or the wrong baud rate). The partial
    /// line is dropped rather than letting the buffer grow without bound.
    /// </summary>
    private const int MaxLineLength = 512;

    /// <summary>
    /// Serialises everything that touches the line buffer and the
    /// duplicate-suppression table.
    ///
    /// SerialPort raises DataReceived on the thread pool, and it does not
    /// promise one at a time: two can run at once. Unguarded, two of them
    /// would each ReadExisting a fragment and then append in whichever
    /// order they happened to get there - interleaving two decoder lines
    /// into one that parses as nothing, or as a transponder that is not on
    /// track. The window is widest exactly when cars cross together, which
    /// is when losing a lap matters most. The Dictionary is not safe for a
    /// concurrent write either.
    ///
    /// Re-entrant, so DrainPort holding it while it calls FeedLine is fine.
    /// </summary>
    private readonly object _lineLock = new();

    /// <summary>
    /// Two lines for the same transponder inside this window are one
    /// physical pass - some decoders repeat a passing record, and a lap
    /// this short isn't a lap on any track this app times.
    /// </summary>
    private const int DuplicateWindowMs = 800;

    /// <summary>How long a connection may hear nothing before the console says so, once.</summary>
    private static readonly TimeSpan NothingReceivedWarning = TimeSpan.FromSeconds(5);

    /// <summary>Set while a reader thread should keep going; cleared first thing on disconnect.</summary>
    private volatile bool _reading;

    /// <summary>Whether this connection has reported the decoder's clock yet - once is enough for the console.</summary>
    private bool _sawClockTick; // guarded by _lineLock

    public event Action<DecoderConnectionState, string>? StatusChanged;
    public event Action<string>? RawMessage;
    public event Action<DirectLapEvent>? LapEventReceived;

    public DecoderConnectionState State { get; private set; } = DecoderConnectionState.Disconnected;

    /// <summary>Serial ports currently present, for the picker. Refreshed on demand - USB adapters come and go.</summary>
    public static string[] AvailablePorts()
    {
        try
        {
            return SerialPort.GetPortNames().Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string CapturePath => Path.Combine(
        AppPaths.Root,
        $"ilap-raw-{DateTime.Now:yyyy-MM-dd}.log");

    public void Connect(string portName, int baudRate)
    {
        Disconnect();

        if (string.IsNullOrWhiteSpace(portName))
        {
            SetState(DecoderConnectionState.Error, "Pick the decoder's COM port first.");
            return;
        }

        SetState(DecoderConnectionState.Connecting, $"Opening {portName} at {baudRate} baud...");

        try
        {
            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 1000,
                NewLine = "\n",
                // Asserted, as a terminal program does. Off by default in
                // .NET, and a USB serial device commonly stays silent until
                // the host raises DTR - on 3 Oct 2026 the I-Lap sent nothing
                // at all to a port opened without it, though the same
                // decoder had streamed passes the night before.
                DtrEnable = true,
                RtsEnable = true,
                Encoding = Encoding.ASCII,
            };
            _port.Open();
        }
        catch (Exception ex)
        {
            _port?.Dispose();
            _port = null;
            SetState(DecoderConnectionState.Error,
                $"Couldn't open {portName}: {ex.Message} If another timing program has the port, close it first.");
            return;
        }

        DeleteOldCaptures();

        lock (_captureLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CapturePath)!);

                // Opened sharing read, write AND delete. StreamWriter's own
                // (path, append) constructor shares read only, which held
                // today's file locked for as long as the decoder was
                // connected - and a backup restore trying to put the
                // backup's copy in its place stopped partway through on it.
                // (The host also closes it before a restore - see
                // BackupRestoreControl.ReleaseFilesForRestore.) Appending,
                // and UTF-8 without a byte order mark, exactly as that
                // constructor did.
                var stream = new FileStream(CapturePath, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                _rawCapture = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
                _captureBytes = _rawCapture.BaseStream.Length;
                if (_captureBytes >= MaxCaptureBytesPerDay)
                {
                    _rawCapture.Dispose();
                    _rawCapture = null;
                    Log($"[ilap] Today's raw capture is already {_captureBytes / (1024 * 1024)} MB - not capturing more today.");
                }
                else
                {
                    WriteCaptureLocked($"--- connected {DateTime.Now:yyyy-MM-dd HH:mm:ss} on {portName} @ {baudRate} ---");
                }
            }
            catch
            {
                // A capture that can't be written is a loss, not a failure -
                // the connection still works and RawMessage still logs.
                _rawCapture?.Dispose();
                _rawCapture = null;
            }
        }

        // Only per-connection state is reset here - the clock (TimingClock) never restarts.
        lock (_lineLock)
        {
            _lastPassMs.Clear();
            _lineBuffer.Clear();
        }

        _sawClockTick = false;

        // Read on a thread of our own, polling as a terminal does, rather
        // than waiting on SerialPort.DataReceived. On 3 Oct 2026 the I-Lap
        // streamed to a PowerShell loop calling ReadExisting on this very
        // port while HeatCaller, waiting on the event, captured nothing at
        // all from it. Polling every 20ms is well inside one character's
        // worth of club-racing precision and does not depend on the USB
        // serial driver raising the event.
        var port = _port;
        _reading = true;
        new Thread(() => ReadLoop(port, portName))
        {
            IsBackground = true,
            Name = "I-Lap reader",
        }.Start();

        SetState(DecoderConnectionState.Connected,
            $"Connected to {portName}. Listening - every line the decoder sends is also captured to {Path.GetFileName(CapturePath)}.");
    }

    /// <summary>
    /// The reader thread: drains the port while this connection is the
    /// current one, and says once if the decoder sends nothing at all.
    /// </summary>
    private void ReadLoop(SerialPort port, string portName)
    {
        var quiet = System.Diagnostics.Stopwatch.StartNew();
        var anything = false;
        var warned = false;

        while (_reading && ReferenceEquals(_port, port))
        {
            int waiting;

            try
            {
                waiting = port.IsOpen ? port.BytesToRead : 0;
            }
            catch (Exception ex)
            {
                if (_reading && ReferenceEquals(_port, port) && State == DecoderConnectionState.Connected)
                {
                    SetState(DecoderConnectionState.Error, $"Read failed: {ex.Message}");
                }

                return;
            }

            if (waiting > 0)
            {
                anything = true;

                if (!DrainPort(port))
                {
                    return;
                }

                continue;
            }

            if (!anything && !warned && quiet.Elapsed > NothingReceivedWarning)
            {
                warned = true;
                Log($"[ilap] Nothing at all has come from {portName} in {NothingReceivedWarning.TotalSeconds:0} seconds. " +
                    "A working I-Lap sends its clock every second, so check this is the I-Lap's port and that nothing else has it open.");
            }

            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// Pulls whatever is waiting and assembles lines. Serial data arrives
    /// in arbitrary fragments, so bytes are buffered until a terminator -
    /// CR, LF, or CRLF all count, because the terminator is one of the
    /// things not being assumed about this device.
    /// </summary>
    private bool DrainPort(SerialPort port)
    {
        try
        {
            lock (_lineLock)
            {
                var chunk = port.ReadExisting();

                foreach (var ch in chunk)
                {
                    if (ch is '\r' or '\n')
                    {
                        if (_lineBuffer.Length > 0)
                        {
                            var line = _lineBuffer.ToString();
                            _lineBuffer.Clear();
                            FeedLine(line);
                        }
                    }
                    else if (_lineBuffer.Length >= MaxLineLength)
                    {
                        Log($"[ilap] Discarded {_lineBuffer.Length} characters with no line ending - check the baud rate.");
                        _lineBuffer.Clear();
                        _lineBuffer.Append(ch);
                    }
                    else
                    {
                        _lineBuffer.Append(ch);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_reading && ReferenceEquals(_port, port) && State == DecoderConnectionState.Connected)
            {
                SetState(DecoderConnectionState.Error, $"Read failed: {ex.Message}");
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// One complete line from the decoder (or from a test). Public so the
    /// parser can be exercised without hardware: paste captured lines
    /// through it and watch what comes out the other end.
    /// </summary>
    public void FeedLine(string line)
    {
        lock (_lineLock)
        {
            FeedLineLocked(line);
        }
    }

    private void FeedLineLocked(string line)
    {
        Capture(line);

        // The decoder's clock, sent every second ("@00000124" is 1:24 since
        // it started). It goes in the capture file but not, every second,
        // into the console - the first one is reported, since it proves the
        // decoder is talking.
        if (IsILapClockTick(line))
        {
            if (!_sawClockTick)
            {
                _sawClockTick = true;
                Log($"[ilap] The decoder is talking - its clock reads {line.Trim().TrimStart('\u0001')}. Waiting for cars.");
            }

            return;
        }

        Log($"[ilap raw] {line}");

        if (ParseLine(line) is { } lap)
        {
            var now = TimingClock.NowMs;

            if (_lastPassMs.TryGetValue(lap.TransponderId, out var last) && now - last < DuplicateWindowMs)
            {
                Log($"[ilap] Suppressed repeat of {lap.TransponderId} ({now - last}ms after the last).");
                return;
            }

            _lastPassMs[lap.TransponderId] = now;
            Log($"[LAP EVENT] transponder={lap.TransponderId}  device_time={lap.DeviceTimeMs}ms");
            LapEventReceived?.Invoke(lap.Stamped(DecoderConnection.ILap, now, TimingPoint.Line));
        }
    }

    /// <summary>
    /// Tolerant reading of one line: strip framing marks, split on the
    /// common separators, and take the first plausible transponder number.
    ///
    /// The time the RACE uses is this client's own monotonic clock at
    /// receipt, not anything parsed from the line. Latency jitter on a
    /// serial line is a millisecond or two - noise at club-racing
    /// precision - and it means lap deltas stay consistent whether or not
    /// the device's own timestamps turn out to be parseable. The captured
    /// raw lines keep the device's times for the day-one protocol read.
    /// </summary>
    private DirectLapEvent? ParseLine(string line)
    {
        // The I-Lap's real passing record, read off the bench on 2 Oct 2026:
        //   SOH '@' TAB decoder TAB transponder TAB seconds CR LF
        //   e.g. "\u0001@\t210\t4385919\t344.699"
        // The tolerant reading below took the FIRST run of 3-8 digits, which
        // here is the decoder's own number (210) - so every car came in as
        // transponder "210" and matched no driver. A line of this shape is
        // now read by position; anything else still falls through to the
        // tolerant reading.
        if (TryParseILapPassing(line) is { } transponder)
        {
            return new DirectLapEvent
            {
                TransponderId = transponder,
                DeviceTimeMs = (uint)TimingClock.NowMs,
                SignalHits = 0,
                SignalStrength = 0,
            };
        }

        var trimmed = line.Trim().TrimStart('\u0001');

        // Anything else from the I-Lap - its once-a-second clock
        // ("@00000124"), or a framed status or heartbeat record - carries
        // numbers too, and the tolerant reading would happily take one for
        // a car: the clock would have been a phantom lap every second. Only
        // the tab-separated passing record above is a pass.
        if (trimmed.StartsWith('@') || line.TrimStart().StartsWith('\u0001'))
        {
            return null;
        }

        var cleaned = trimmed.TrimStart('@', '%', '$', '#', '*', '>');

        if (cleaned.Length == 0)
        {
            return null;
        }

        var tokens = cleaned.Split(
            new[] { ' ', '\t', ',', ';', ':' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            // A transponder reads as a run of 3-8 digits. Shorter is a
            // status code or a hit count; longer is a timestamp.
            if (token.Length is >= 3 and <= 8 && token.All(char.IsAsciiDigit))
            {
                return new DirectLapEvent
                {
                    TransponderId = token.TrimStart('0').Length > 0 ? token.TrimStart('0') : token,
                    DeviceTimeMs = (uint)TimingClock.NowMs,
                    // 0 = unknown. Nothing in the line is known to be a hit
                    // count yet, and TransponderWatch treats any value from 1
                    // to 2 as a marginal pass - claiming 1 made every I-Lap
                    // transponder look weak in the readiness check.
                    SignalHits = 0,
                    SignalStrength = 0,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// The transponder from an I-Lap passing record - the long form
    /// ("\u0001@\tdecoder\ttransponder\tseconds") or the short one ("@TTHHMMSScc42"),
    /// or null when the line is not one. Leading zeros are dropped, as in the tolerant reading.
    /// </summary>
    internal static string? TryParseILapPassing(string line)
    {
        var trimmed = line.Trim().TrimStart('\u0001');

        if (!trimmed.StartsWith('@'))
        {
            return null;
        }

        // The short form, sent once the decoder has been reset by the port
        // opening (3 Oct 2026, beside its once-a-second clock):
        //   "@040000070442" = '@', transponder 04, then 00:00:07.04 on the
        //   decoder's clock, then 42.
        // Two digits of transponder: 04 is read as "4". The time is not used
        // - laps are timed on receipt, like every other I-Lap line.
        if (trimmed.Length == 13 && trimmed.Skip(1).All(char.IsAsciiDigit))
        {
            var shortId = trimmed.Substring(1, 2).TrimStart('0');
            return shortId.Length > 0 ? shortId : "0";
        }

        var fields = trimmed.Split('\t', StringSplitOptions.TrimEntries);

        // '@', decoder, transponder, time - the time is not needed, but a
        // record cut short before it is still a pass worth counting.
        if (fields.Length < 3 || fields[0] != "@")
        {
            return null;
        }

        var id = fields[2];

        if (id.Length is < 1 or > 10 || !id.All(char.IsAsciiDigit))
        {
            return null;
        }

        var stripped = id.TrimStart('0');
        return stripped.Length > 0 ? stripped : id;
    }

    /// <summary>
    /// The I-Lap's once-a-second clock: '@' then exactly eight digits, e.g.
    /// "@00000124" - minutes and seconds since the decoder started. Eight
    /// exactly: a passing in the short form is '@' and twelve digits.
    /// </summary>
    internal static bool IsILapClockTick(string line)
    {
        var trimmed = line.Trim().TrimStart('\u0001');
        return trimmed.Length == 9 && trimmed[0] == '@' && trimmed.Skip(1).All(char.IsAsciiDigit);
    }

    private void Capture(string line)
    {
        lock (_captureLock)
        {
            try
            {
                WriteCaptureLocked($"{DateTime.Now:HH:mm:ss.fff}  {line}");
            }
            catch
            {
                // Best-effort record; never let logging break timing.
            }
        }
    }

    /// <summary>Writes one capture line, stopping the capture once the day's file reaches its cap. Caller holds _captureLock.</summary>
    private void WriteCaptureLocked(string text)
    {
        if (_rawCapture is null)
        {
            return;
        }

        // StreamWriter's default encoding is UTF-8 (no BOM); +2 covers the newline.
        var bytes = Encoding.UTF8.GetByteCount(text) + 2;
        if (_captureBytes + bytes > MaxCaptureBytesPerDay)
        {
            try
            {
                _rawCapture.WriteLine($"--- capture stopped: file reached {MaxCaptureBytesPerDay / (1024 * 1024)} MB ---");
            }
            finally
            {
                _rawCapture.Dispose();
                _rawCapture = null;
            }
            Log($"[ilap] Raw capture file reached {MaxCaptureBytesPerDay / (1024 * 1024)} MB - capture stopped; timing is unaffected.");
            return;
        }

        _rawCapture.WriteLine(text);
        _captureBytes += bytes;
    }

    /// <summary>Best-effort removal of raw capture files past their retention.</summary>
    private void DeleteOldCaptures()
    {
        try
        {
            var folder = Path.GetDirectoryName(CapturePath)!;
            if (!Directory.Exists(folder))
            {
                return;
            }

            var cutoff = DateTime.Now - CaptureRetention;
            foreach (var file in Directory.EnumerateFiles(folder, "ilap-raw-*.log"))
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
                    // In use or no permission - leave it for next time.
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[ilap] Could not tidy old capture files: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        // First, so the reader thread stops before the port goes away
        // under it and does not report that as a read failure.
        _reading = false;

        if (_port is not null)
        {
            try
            {
                _port.Dispose();
            }
            catch
            {
                // ignore - tearing down anyway
            }

            _port = null;
        }

        lock (_captureLock)
        {
            _rawCapture?.Dispose();
            _rawCapture = null;
        }

        lock (_lineLock)
        {
            _lineBuffer.Clear();
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

    public void Dispose() => Disconnect();
}
