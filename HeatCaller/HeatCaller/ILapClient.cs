using System.Collections.Concurrent;
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

    /// <summary>
    /// Capture lines waiting for the writer thread, or null when nothing is
    /// being captured. Replaced on every connect.
    ///
    /// Bounded, as SessionLog's is, so a writer that has stopped (a full or
    /// failed disk) can never grow memory without limit; past it, lines are
    /// dropped rather than the reader made to wait on the disk.
    /// </summary>
    private volatile BlockingCollection<string>? _capturePending;

    /// <summary>The thread writing them. Guarded, with the queue, by _captureLock.</summary>
    private Thread? _captureThread;

    private readonly object _captureLock = new();
    private readonly string _captureFolder;

    /// <summary>1 while a connect is in flight on a thread of its own - see Connect.</summary>
    private int _connecting;

    /// <summary>
    /// Bumped by every Connect and every Disconnect. A connect that comes
    /// back out of the serial stack to find this number moved on is working
    /// for a connection nobody wants any more: it closes the port it opened
    /// rather than publishing it over what happened in the meantime.
    /// </summary>
    private int _generation;

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

    /// <summary>How many capture lines may wait for the writer before they start being dropped. As SessionLog's.</summary>
    private const int CaptureQueueLimit = 50_000;

    /// <summary>
    /// How long a disconnect waits for the capture writer to flush and close
    /// the file. Short and bounded - see StopCapture.
    /// </summary>
    private static readonly TimeSpan CaptureCloseWait = TimeSpan.FromMilliseconds(250);

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
    /// Re-entrant, so DrainPort holding it while it calls FeedLineLocked is
    /// fine. What it must NOT hold it across is the lap event, which ends in
    /// the whole race pipeline - see DrainPort.
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

    /// <summary>
    /// Where the raw capture log is written.
    ///
    /// Passed in rather than read from AppPaths, because this class decodes
    /// a serial line and should not also know where the app keeps the
    /// club's data. It was the last thing tying the line decoder to the
    /// app's storage layout, and that mattered: the I-Lap is the LINE
    /// decoder now, so a test wants to build one, feed it a recorded night
    /// and check the laps - which it cannot do if constructing one reaches
    /// for the real data folder.
    ///
    /// Left optional so a test can build one without choosing a folder; it
    /// then captures under the temp directory, where it is harmless. The
    /// app passes AppPaths.Root, so the club's capture files land exactly
    /// where they always have.
    /// </summary>
    public ILapClient(string? captureFolder = null)
    {
        _captureFolder = captureFolder ?? Path.Combine(Path.GetTempPath(), "HeatCaller", "ilap-capture");
    }

    private string CapturePath => Path.Combine(
        _captureFolder,
        $"ilap-raw-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>
    /// Starts connecting to the decoder on <paramref name="portName"/>, and
    /// hands back as soon as that is under way - with <see cref="State"/>
    /// already Connecting.
    ///
    /// Opening and closing the port used to happen on the caller's thread,
    /// and the caller is the UI thread: the Connect button, and the
    /// auto-connect timer's tick every three seconds. Both calls can block
    /// for a very long time with nothing to cut them short.
    /// SerialPort.Open() on a Bluetooth SPP port routinely takes ten to
    /// thirty seconds while the stack pairs - and the COM port list is full
    /// of them, see MainForm.Connection - and SerialPort.Dispose() on a USB
    /// adapter that has been unplugged is a long-standing unbounded hang in
    /// .NET. Either one froze the window with nothing said and almost no
    /// CPU, which is what rehearsal 2 saw and what HangWatchdog was shipped
    /// to catch. Only the state and the status event are left on the
    /// caller's thread now.
    ///
    /// What callers see is unchanged: they set the buttons around this and
    /// then read State, and State is Connecting the instant this returns.
    /// That is also what keeps the three-second tick from asking again while
    /// one is still in flight - it acts only on Disconnected or Error.
    /// </summary>
    public void Connect(string portName, int baudRate)
    {
        // Before anything is taken down, since there is nothing to take it
        // down for.
        if (string.IsNullOrWhiteSpace(portName))
        {
            SetState(DecoderConnectionState.Error, "Pick the decoder's COM port first.");
            return;
        }

        if (Interlocked.Exchange(ref _connecting, 1) == 1)
        {
            // One at a time. Two would open two ports and start two reader
            // threads on one decoder. Nothing is said about it: the
            // three-second tick would fill the console with it.
            return;
        }

        var generation = Interlocked.Increment(ref _generation);

        SetState(DecoderConnectionState.Connecting, $"Opening {portName} at {baudRate} baud...");

        new Thread(() => ConnectCore(portName, baudRate, generation))
        {
            IsBackground = true,
            Name = "I-Lap connect",
        }.Start();
    }

    /// <summary>
    /// The connect itself, on a thread of its own: close the old port, open
    /// the new one, start the capture and the reader. Everything in here is
    /// allowed to block for as long as the serial stack likes.
    /// </summary>
    /// <param name="generation">
    /// This attempt's number - see <see cref="_generation"/>. Once it has
    /// moved on, this attempt says nothing and keeps nothing.
    /// </param>
    private void ConnectCore(string portName, int baudRate, int generation)
    {
        void Report(DecoderConnectionState state, string message)
        {
            // Nothing is published on behalf of an attempt that has been
            // overtaken: the Disconnect that overtook it has had the last
            // word, and an Error landing after it would turn the status
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

            SerialPort port;

            try
            {
                port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
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
                port.Open();
            }
            catch (Exception ex)
            {
                Report(DecoderConnectionState.Error,
                    $"Couldn't open {portName}: {ex.Message} If another timing program has the port, close it first.");
                return;
            }

            // Opened for nobody: a Disconnect - or a later Connect - while
            // the serial stack had us. Closed again rather than published
            // over whatever is there now, which is how two reader threads on
            // one decoder would otherwise start.
            if (Volatile.Read(ref _generation) != generation)
            {
                CloseQuietly(port);
                return;
            }

            DeleteOldCaptures();
            StartCapture(portName, baudRate);

            // Only per-connection state is reset here - the clock (TimingClock) never restarts.
            lock (_lineLock)
            {
                _lastPassMs.Clear();
                _lineBuffer.Clear();
                _sawClockTick = false;
            }

            // Read on a thread of our own, polling as a terminal does, rather
            // than waiting on SerialPort.DataReceived. On 3 Oct 2026 the I-Lap
            // streamed to a PowerShell loop calling ReadExisting on this very
            // port while HeatCaller, waiting on the event, captured nothing at
            // all from it. Polling every 20ms is well inside one character's
            // worth of club-racing precision and does not depend on the USB
            // serial driver raising the event.
            //
            // Connected is said BEFORE the reader starts, as DirectDecoderClient
            // does. The other way round, a read that failed in the moment
            // between the two found the state still Connecting, said nothing,
            // and the reader ended - then Connected was published over it: a
            // green light with nothing reading the port.
            _port = port;
            _reading = true;

            // Looked at once more now the port IS the live one: a Disconnect
            // landing between the look above and this assignment would have
            // found nothing to close, and the reader would have started on a
            // port nobody wanted.
            if (Volatile.Read(ref _generation) != generation)
            {
                Teardown();
                return;
            }

            SetState(DecoderConnectionState.Connected,
                $"Connected to {portName}. Listening - every line the decoder sends is also captured to {Path.GetFileName(CapturePath)}.");

            new Thread(() => ReadLoop(port, portName))
            {
                IsBackground = true,
                Name = "I-Lap reader",
            }.Start();
        }
        catch (Exception ex)
        {
            // Nobody waits on this thread, so anything escaping here would
            // be an unhandled exception and the end of the app.
            try
            {
                Report(DecoderConnectionState.Error, $"Connecting to {portName} failed: {ex.Message}");
            }
            catch
            {
                // A subscriber threw while reporting the failure - nothing more we can do here.
            }
        }
        finally
        {
            // Cleared last, so the next tick may try again. A serial call
            // that NEVER returns leaves this set and no further connect is
            // attempted - which is the right answer: the window is free
            // either way, and asking a wedged port again only wedges another
            // thread behind the first.
            Volatile.Write(ref _connecting, 0);
        }
    }

    /// <summary>Closes a port a connect opened and is not going to use. Never throws.</summary>
    private static void CloseQuietly(SerialPort port)
    {
        try
        {
            port.Dispose();
        }
        catch
        {
            // ignore - nothing is reading it
        }
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
                if (_reading && ReferenceEquals(_port, port) && State is not (DecoderConnectionState.Disconnected or DecoderConnectionState.Error))
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
        // The passes this chunk turned out to hold. Collected under the lock
        // and raised below it: LapEventReceived ends in MainForm.OnILapPass
        // and from there in the whole race pipeline, so raising it from in
        // here meant every subscriber's work - and anything any of them
        // waited on - was done holding the lock that Disconnect() waits for
        // on the UI thread.
        List<DirectLapEvent>? passes = null;
        var ok = true;

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

                            if (FeedLineLocked(line) is { } pass)
                            {
                                (passes ??= new List<DirectLapEvent>()).Add(pass);
                            }
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
            if (_reading && ReferenceEquals(_port, port) && State is not (DecoderConnectionState.Disconnected or DecoderConnectionState.Error))
            {
                SetState(DecoderConnectionState.Error, $"Read failed: {ex.Message}");
            }

            ok = false;
        }

        // Raised even when the read then failed: a pass already assembled
        // from the bytes that did arrive is a lap a driver really did, and
        // it is not thrown away because the port went afterwards.
        if (passes is not null)
        {
            foreach (var pass in passes)
            {
                RaisePass(pass);
            }
        }

        return ok;
    }

    /// <summary>
    /// One complete line from the decoder (or from a test). Public so the
    /// parser can be exercised without hardware: paste captured lines
    /// through it and watch what comes out the other end.
    /// </summary>
    public void FeedLine(string line)
    {
        DirectLapEvent? pass;

        lock (_lineLock)
        {
            pass = FeedLineLocked(line);
        }

        RaisePass(pass);
    }

    /// <summary>
    /// Reads one line and hands back the pass it carried, or null. Raising
    /// it is the caller's job, once _lineLock is free - see DrainPort.
    /// Caller holds _lineLock.
    /// </summary>
    private DirectLapEvent? FeedLineLocked(string line)
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

            return null;
        }

        Log($"[ilap raw] {line}");

        if (ParseLine(line) is { } lap)
        {
            var now = TimingClock.NowMs;

            if (_lastPassMs.TryGetValue(lap.TransponderId, out var last) && now - last < DuplicateWindowMs)
            {
                Log($"[ilap] Suppressed repeat of {lap.TransponderId} ({now - last}ms after the last).");
                return null;
            }

            _lastPassMs[lap.TransponderId] = now;
            Log($"[LAP EVENT] transponder={lap.TransponderId}  device_time={lap.DeviceTimeMs}ms");
            return lap.Stamped(DecoderConnection.ILap, now, TimingPoint.Line);
        }

        return null;
    }

    /// <summary>Raises LapEventReceived for a pass, with _lineLock free. Does nothing for null.</summary>
    private void RaisePass(DirectLapEvent? pass)
    {
        if (pass is null)
        {
            return;
        }

        LapEventReceived?.Invoke(pass);
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

    /// <summary>
    /// Hands one line to the capture writer. Never waits on the disk, never
    /// throws.
    ///
    /// This used to write the line itself, to a StreamWriter with AutoFlush
    /// on - one synchronous flush to disk per decoder line, and the I-Lap
    /// sends its clock every second on top of every pass. It runs under
    /// _lineLock, and Disconnect() waits on that same lock from the UI
    /// thread, so on a slow or failing volume the window stalled for as long
    /// as the disk took, with nothing to bound it. See StartCapture.
    /// </summary>
    private void Capture(string line)
    {
        var pending = _capturePending;

        if (pending is null)
        {
            return;
        }

        try
        {
            // Dropped rather than waited for when the queue is full: a
            // writer that has stopped must not stop the reader with it.
            pending.TryAdd($"{DateTime.Now:HH:mm:ss.fff}  {line}");
        }
        catch
        {
            // The queue was completed by a disconnect in the same moment.
            // Best-effort record; never let logging break timing.
        }
    }

    /// <summary>
    /// Opens today's raw capture file and starts the thread that writes it.
    /// On the connect thread, before the reader starts.
    ///
    /// Written on a thread of its own, never the one that read the line -
    /// the same shape, and for the same reason, as SessionLog: the UI thread
    /// must not wait on the disk, and nor must the thread assembling laps.
    /// </summary>
    private void StartCapture(string portName, int baudRate)
    {
        lock (_captureLock)
        {
            // Outside the try so the catch can close it: a file left open
            // because starting the thread failed would hold today's capture
            // locked for the rest of the run.
            StreamWriter? writer = null;

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
                writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                var already = stream.Length;

                if (already >= MaxCaptureBytesPerDay)
                {
                    writer.Dispose();
                    Log($"[ilap] Today's raw capture is already {already / (1024 * 1024)} MB - not capturing more today.");
                    return;
                }

                var pending = new BlockingCollection<string>(boundedCapacity: CaptureQueueLimit);
                pending.Add($"--- connected {DateTime.Now:yyyy-MM-dd HH:mm:ss} on {portName} @ {baudRate} ---");

                var started = writer;

                var thread = new Thread(() => CaptureLoop(started, pending, already))
                {
                    IsBackground = true,
                    Name = "I-Lap capture writer",
                };

                _capturePending = pending;
                _captureThread = thread;

                thread.Start();

                // Handed over: the writer thread owns it from here and the
                // catch below must not close it.
                writer = null;
            }
            catch
            {
                // A capture that can't be written is a loss, not a failure -
                // the connection still works and RawMessage still logs.
                _capturePending = null;
                _captureThread = null;

                try
                {
                    writer?.Dispose();
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// The capture writer thread: one per connection, ending when that
    /// connection's queue is completed (see StopCapture) or when the day's
    /// file reaches its cap. Never throws out of here.
    /// </summary>
    private void CaptureLoop(StreamWriter writer, BlockingCollection<string> pending, long startBytes)
    {
        var written = startBytes;

        try
        {
            using (writer)
            {
                foreach (var text in pending.GetConsumingEnumerable())
                {
                    // StreamWriter's default encoding is UTF-8 (no BOM); +2 covers the newline.
                    var bytes = Encoding.UTF8.GetByteCount(text) + 2;

                    if (written + bytes > MaxCaptureBytesPerDay)
                    {
                        writer.WriteLine($"--- capture stopped: file reached {MaxCaptureBytesPerDay / (1024 * 1024)} MB ---");
                        Log($"[ilap] Raw capture file reached {MaxCaptureBytesPerDay / (1024 * 1024)} MB - capture stopped; timing is unaffected.");
                        return;
                    }

                    writer.WriteLine(text);
                    written += bytes;

                    // Flushed whenever the queue runs dry, which during a
                    // race is after almost every line - the same bargain
                    // SessionLog makes, and what AutoFlush used to do on the
                    // reader's thread under the line lock.
                    if (pending.Count == 0)
                    {
                        writer.Flush();
                    }
                }
            }
        }
        catch
        {
            // The disk went away or filled up. Stop quietly; the bounded
            // queue drops what follows rather than growing.
        }
        finally
        {
            // Nothing more is captured on this connection, so Capture stops
            // queueing lines no thread will ever write. Only if the queue is
            // still this connection's - a later connect has its own.
            lock (_captureLock)
            {
                if (ReferenceEquals(_capturePending, pending))
                {
                    _capturePending = null;
                }
            }
        }
    }

    /// <summary>
    /// Ends the capture: the writer is told nothing more is coming and given
    /// a moment to flush and close the file.
    ///
    /// The wait is short and bounded ON PURPOSE. A backup restore closes the
    /// capture by disconnecting (see MainForm's ReleaseFilesForRestore) and
    /// expects the file to be free afterwards, which is worth a moment - but
    /// this can be the UI thread, and the whole point of moving the writing
    /// off it was that a failing disk must never stall the window. Past the
    /// wait the writer is left to close the file in its own time, and the
    /// file is shared for delete in any case.
    /// </summary>
    private void StopCapture()
    {
        BlockingCollection<string>? pending;
        Thread? writer;

        // The references are taken under the lock and the waiting done
        // outside it: the writer takes the same lock on its way out.
        lock (_captureLock)
        {
            pending = _capturePending;
            writer = _captureThread;
            _capturePending = null;
            _captureThread = null;
        }

        try
        {
            pending?.CompleteAdding();
        }
        catch
        {
            // Already completed - nothing to end.
        }

        writer?.Join(CaptureCloseWait);
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
        // Any connect still waiting on the serial stack is now working for a
        // connection nobody wants - see ConnectCore.
        Interlocked.Increment(ref _generation);

        Teardown();

        if (State != DecoderConnectionState.Disconnected)
        {
            SetState(DecoderConnectionState.Disconnected, "Disconnected.");
        }
    }

    /// <summary>
    /// Stops the reader, lets the port go and ends the capture, without
    /// touching <see cref="State"/>. Its own method because a connect does
    /// this too, on its own thread, and must not publish Disconnected in the
    /// middle of connecting.
    /// </summary>
    private void Teardown()
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

        StopCapture();

        lock (_lineLock)
        {
            _lineBuffer.Clear();
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
