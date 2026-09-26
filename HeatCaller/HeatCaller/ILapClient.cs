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
    // Started once, here, and never restarted. DeviceTimeMs is read from
    // this clock and the race session measures laps as differences between
    // successive readings - restarting it on a reconnect would make the
    // clock jump backwards mid-race and every later lap come out negative.
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
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

        // Only per-connection state is reset here - NOT _clock (see its comment).
        lock (_lineLock)
        {
            _lastPassMs.Clear();
            _lineBuffer.Clear();
        }

        var port = _port;
        port.DataReceived += (_, _) => DrainPort(port);

        SetState(DecoderConnectionState.Connected,
            $"Connected to {portName}. Listening - every line the decoder sends is also captured to {Path.GetFileName(CapturePath)}.");
    }

    /// <summary>
    /// Pulls whatever is waiting and assembles lines. Serial data arrives
    /// in arbitrary fragments, so bytes are buffered until a terminator -
    /// CR, LF, or CRLF all count, because the terminator is one of the
    /// things not being assumed about this device.
    /// </summary>
    private void DrainPort(SerialPort port)
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
            if (State == DecoderConnectionState.Connected)
            {
                SetState(DecoderConnectionState.Error, $"Read failed: {ex.Message}");
            }
        }
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
        Log($"[ilap raw] {line}");

        if (ParseLine(line) is { } lap)
        {
            var now = _clock.ElapsedMilliseconds;

            if (_lastPassMs.TryGetValue(lap.TransponderId, out var last) && now - last < DuplicateWindowMs)
            {
                Log($"[ilap] Suppressed repeat of {lap.TransponderId} ({now - last}ms after the last).");
                return;
            }

            _lastPassMs[lap.TransponderId] = now;
            Log($"[LAP EVENT] transponder={lap.TransponderId}  device_time={lap.DeviceTimeMs}ms");
            LapEventReceived?.Invoke(lap);
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
        var cleaned = line.Trim().TrimStart('@', '%', '$', '#', '*', '>');

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
                    DeviceTimeMs = (uint)_clock.ElapsedMilliseconds,
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
