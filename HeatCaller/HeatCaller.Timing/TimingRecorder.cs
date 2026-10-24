using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// One line of a timing recording (see <see cref="TimingRecorder"/>): the
/// file's header, a pass from the decoder (or a ghost, or the demo) as it
/// reached the race, or a command the race was given - Stage, the green, a
/// red, a lap added by hand, a warning, an undo.
///
/// Every line carries when it was recorded twice over: on the app's own
/// monotonic clock (ClockMs, TimingClock) and on the wall clock (Utc). For
/// a pass that is the moment the race PROCESSED it; when it reached the app
/// is ReceivedMs, and the gap between the two is how late it was.
///
/// Only the fields that mean something for the line's kind are written; the
/// rest stay null and are left out of the file.
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer, P0): the safety net for moving timing to
/// its own thread. A recorded night can be played back through a fresh
/// RaceControlSession (the tests' replay harness), so a change to how passes
/// are delivered can be proved not to change a result.
/// </remarks>
public sealed class TimingRecord
{
    /// <summary>The format written today. Bumped only if a field changes meaning.</summary>
    public const int CurrentVersion = 1;

    public const string HeaderKind = "header";
    public const string PassKind = "pass";
    public const string CommandKind = "command";

    /// <summary>"header", "pass" or "command".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>1, 2, 3... in the order the lines were recorded. The header is 0.</summary>
    public long Seq { get; set; }

    /// <summary>When it was recorded, on TimingClock (ms since the app started).</summary>
    public long ClockMs { get; set; }

    /// <summary>When it was recorded, on the wall clock.</summary>
    public DateTime Utc { get; set; }

    // --- The header -----------------------------------------------------------------

    /// <summary>The format version (header only).</summary>
    public int? Version { get; set; }

    /// <summary>Which HeatCaller wrote it (header only).</summary>
    public string? App { get; set; }

    // --- A pass ---------------------------------------------------------------------

    /// <summary>The transponder as the decoder reported it (a pass), or the driver a command was for.</summary>
    public string? Transponder { get; set; }

    /// <summary>The decoder's own time of the pass (DirectLapEvent.DeviceTimeMs), or the device time a green was stamped with.</summary>
    public long? DeviceTimeMs { get; set; }

    /// <summary>When the pass reached the app, on TimingClock (DirectLapEvent.ReceivedMs) - 0 when nobody stamped it.</summary>
    public long? ReceivedMs { get; set; }

    /// <summary>Which decoder saw it (DecoderConnection), 0 for none.</summary>
    public int? Decoder { get; set; }

    public int? SignalHits { get; set; }

    public int? SignalStrength { get; set; }

    /// <summary>True for a pass made up inside the app - a ghost (DirectLapEvent.IsSynthetic).</summary>
    public bool? Synthetic { get; set; }

    /// <summary>Where on the track (TimingPoint) - the line, for every pass the race sees.</summary>
    public int? Point { get; set; }

    /// <summary>
    /// Whose alternate transponder this was at that moment (the profiles'
    /// answer to RaceControlSession.AlternateTransponderOwner), or null when
    /// it was nobody's - so a replay files it the same way without the
    /// profiles.
    /// </summary>
    public string? AlternateOf { get; set; }

    // --- A command ------------------------------------------------------------------

    /// <summary>Which command: Stage, Countdown, Config, Green, Resume, Red, Yellow, Checkered, Reset, AdjustLaps, Penalty, ClearPenalties, Warning, RemoveWarning, Release, ReleaseAll, SetGrid, SetReleaseSchedule, Snapshot, Restore, RestoreAfterGap, UndoEdit, RemoveGhost.</summary>
    public string? Command { get; set; }

    /// <summary>The command's number: laps added (AdjustLaps), ms of penalty (Penalty).</summary>
    public long? Value { get; set; }

    /// <summary>The command's words: a warning's reason.</summary>
    public string? Text { get; set; }

    /// <summary>SetGrid's grid, in order.</summary>
    public List<string>? Grid { get; set; }

    /// <summary>SetReleaseSchedule's offsets, ms after the green, by transponder.</summary>
    public Dictionary<string, long>? Offsets { get; set; }

    /// <summary>The race's rules as they stood (Config) - the time and lap limits, start type and the rest. See RaceControlSession.RecordConfig.</summary>
    public Dictionary<string, string>? Config { get; set; }

    /// <summary>Which Snapshot line a Restore puts back - that line's Seq. Null for one nobody recorded (a crash journal).</summary>
    public long? SnapshotId { get; set; }

    /// <summary>RestoreAfterGap's "taken at" - when the snapshot it puts back was taken.</summary>
    public DateTime? TakenUtc { get; set; }

    /// <summary>The race's state and flag just before the command ran.</summary>
    public string? State { get; set; }

    public string? Flag { get; set; }
}

/// <summary>
/// Records every pass the race is given and every command it is told, one
/// JSON object per line (see <see cref="TimingRecord"/>), for playing a
/// night back later. Off unless AppSettings.RecordTimingPasses is ticked.
///
/// Pure and thread-safe: it numbers each line and hands it to a sink under
/// one lock, so lines from any thread come out whole and in order. It never
/// throws - a recording is a diagnostic, and nothing about it may stop a
/// lap being counted.
///
/// The file sink (<see cref="StartFile"/>) writes on a thread of its own,
/// exactly as the I-Lap's raw capture does (ILapClient.StartCapture): the
/// thread counting laps never waits on the disk, a full queue drops lines
/// rather than growing, and the file stops at <see cref="MaxFileBytes"/>.
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer, P0). File format: UTF-8 without a byte
/// order mark, one object per line, camelCase names, nulls left out. The
/// first line is the header {"kind":"header","seq":0,"clockMs":...,
/// "utc":"...","version":1,"app":"..."}; then, for example,
/// {"kind":"pass","seq":7,"clockMs":181234,"utc":"...","transponder":"4385919",
/// "deviceTimeMs":5123456,"receivedMs":181230,"decoder":1,"signalHits":40,
/// "signalStrength":120,"synthetic":false,"point":0} and
/// {"kind":"command","seq":8,"clockMs":181300,"utc":"...","command":"Red",
/// "state":"Racing","flag":"Green"}.
/// </remarks>
public sealed class TimingRecorder : IDisposable
{
    /// <summary>Where the recordings go: under this PC's own data, not the club's, so they never swell a backup.</summary>
    public static string DefaultFolder => Path.Combine(AppPaths.LocalRoot, "timing-recordings");

    /// <summary>A file is closed off at this size - far more than any night, so reaching it means something is wrong.</summary>
    public const long MaxFileBytes = 64L * 1024 * 1024;

    /// <summary>Recordings older than this are deleted when a new one starts.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>How many lines may wait for the writer before they start being dropped.</summary>
    private const int QueueLimit = 100_000;

    /// <summary>How long Dispose waits for the writer to flush - short, because it can be the UI thread.</summary>
    private static readonly TimeSpan CloseWait = TimeSpan.FromMilliseconds(250);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly object _gate = new();
    private readonly Action<string> _sink;
    private readonly Action? _close;
    private long _seq;
    private bool _disposed;

    /// <summary>
    /// A recorder that hands each line to <paramref name="writeLine"/> - a
    /// list in a test, the file writer in the app. The header goes first,
    /// stamped at <paramref name="clockMs"/> and <paramref name="utc"/> (now,
    /// when left out).
    /// </summary>
    public TimingRecorder(Action<string> writeLine, long? clockMs = null, DateTime? utc = null)
        : this(writeLine, null, null, clockMs, utc)
    {
    }

    private TimingRecorder(Action<string> writeLine, Action? close, string? filePath, long? clockMs, DateTime? utc)
    {
        _sink = writeLine;
        _close = close;
        FilePath = filePath;

        Emit(new TimingRecord
        {
            Kind = TimingRecord.HeaderKind,
            Version = TimingRecord.CurrentVersion,
            App = SafeAppVersion(),
        }, clockMs ?? TimingClock.NowMs, utc ?? DateTime.UtcNow, numbered: false);
    }

    /// <summary>The file being written, or null for a recorder with its own sink.</summary>
    public string? FilePath { get; }

    /// <summary>
    /// Starts a new recording file in <paramref name="folder"/> (DefaultFolder
    /// when left out), named for the local time it started, and deletes
    /// recordings past <see cref="Retention"/>. Null, with the reason given to
    /// <paramref name="log"/>, when the file cannot be opened - the race runs
    /// exactly the same without one.
    /// </summary>
    public static TimingRecorder? StartFile(string? folder = null, Action<string>? log = null)
    {
        folder ??= DefaultFolder;
        StreamWriter? writer = null;

        try
        {
            Directory.CreateDirectory(folder);
            DeleteOld(folder);

            var path = Path.Combine(folder, $"passes-{DateTime.Now:yyyyMMdd-HHmmss}{(AppPaths.IsRehearsal ? "-rehearsal" : string.Empty)}.jsonl");

            // Shared for read and delete, so a support bundle or a curious
            // director can open it while the night is still being written.
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var pending = new BlockingCollection<string>(boundedCapacity: QueueLimit);
            var started = writer;
            var thread = new Thread(() => WriteLoop(started, pending, log))
            {
                IsBackground = true,
                Name = "Timing recorder writer",
            };

            thread.Start();
            writer = null; // the writer thread owns it from here

            void Close()
            {
                try
                {
                    pending.CompleteAdding();
                }
                catch
                {
                    // Already completed.
                }

                thread.Join(CloseWait);
            }

            log?.Invoke($"[timing] Recording every pass and race command to {path}.");

            return new TimingRecorder(
                line =>
                {
                    try
                    {
                        // Dropped rather than waited for: a stopped writer
                        // must never stop the race with it.
                        pending.TryAdd(line);
                    }
                    catch
                    {
                        // Completed by Dispose in the same moment.
                    }
                },
                Close,
                path,
                null,
                null);
        }
        catch (Exception ex)
        {
            try
            {
                writer?.Dispose();
            }
            catch
            {
            }

            log?.Invoke($"[timing] The pass recording could not be started ({ex.Message}) - timing carries on unrecorded.");
            return null;
        }
    }

    /// <summary>
    /// Records a pass as the race is given it, at <paramref name="clockMs"/>
    /// (TimingClock) and <paramref name="utc"/>. Returns its Seq.
    /// </summary>
    public long Pass(DirectLapEvent lap, string? alternateOf, long clockMs, DateTime utc) =>
        Emit(new TimingRecord
        {
            Kind = TimingRecord.PassKind,
            Transponder = lap.TransponderId,
            DeviceTimeMs = lap.DeviceTimeMs,
            ReceivedMs = lap.ReceivedMs,
            Decoder = lap.Decoder,
            SignalHits = lap.SignalHits,
            SignalStrength = lap.SignalStrength,
            Synthetic = lap.IsSynthetic,
            Point = (int)lap.Point,
            AlternateOf = string.IsNullOrWhiteSpace(alternateOf) ? null : alternateOf,
        }, clockMs, utc, numbered: true);

    /// <summary>
    /// Records a command (<paramref name="record"/>'s Command and whatever
    /// else it carries; its Kind, Seq and stamps are filled in here). Returns
    /// its Seq - the id a Snapshot is restored by.
    /// </summary>
    public long Command(TimingRecord record, long clockMs, DateTime utc)
    {
        record.Kind = TimingRecord.CommandKind;
        return Emit(record, clockMs, utc, numbered: true);
    }

    private long Emit(TimingRecord record, long clockMs, DateTime utc, bool numbered)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return 0;
            }

            record.Seq = numbered ? ++_seq : 0;
            record.ClockMs = clockMs;
            record.Utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

            try
            {
                // Under the lock, so lines reach the sink in Seq order.
                _sink(Serialize(record));
            }
            catch
            {
                // A recording is never worth a lap.
            }

            return record.Seq;
        }
    }

    /// <summary>One record as one line of JSON.</summary>
    public static string Serialize(TimingRecord record) => JsonSerializer.Serialize(record, Json);

    /// <summary>One line back into a record, or null for a blank or unreadable one.</summary>
    public static TimingRecord? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TimingRecord>(line, Json);
        }
        catch (JsonException)
        {
            // A line cut short by a crash mid-write - the rest still plays.
            return null;
        }
    }

    /// <summary>Every readable line of a recording, in the order written.</summary>
    public static List<TimingRecord> ReadAll(IEnumerable<string> lines)
    {
        var records = new List<TimingRecord>();

        foreach (var line in lines)
        {
            if (Parse(line) is { } record)
            {
                records.Add(record);
            }
        }

        return records;
    }

    /// <summary>Stops recording; the file writer is given a moment to flush. Safe to call twice.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            _close?.Invoke();
        }
        catch
        {
        }
    }

    private static void WriteLoop(StreamWriter writer, BlockingCollection<string> pending, Action<string>? log)
    {
        long written = 0;

        try
        {
            using (writer)
            {
                foreach (var text in pending.GetConsumingEnumerable())
                {
                    var bytes = Encoding.UTF8.GetByteCount(text) + 2;

                    if (written + bytes > MaxFileBytes)
                    {
                        log?.Invoke($"[timing] The pass recording reached {MaxFileBytes / (1024 * 1024)} MB and stopped; timing is unaffected.");
                        return;
                    }

                    writer.WriteLine(text);
                    written += bytes;

                    // Flushed whenever the queue runs dry, so a crash loses
                    // at most what was still waiting.
                    if (pending.Count == 0)
                    {
                        writer.Flush();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // The disk went away or filled up. Stop - and say so once.
            try
            {
                log?.Invoke($"[timing] The pass recording stopped ({ex.GetType().Name}: {ex.Message}); timing is unaffected.");
            }
            catch
            {
            }
        }
        finally
        {
            // WHY (24 Oct 2026, timing bug hunt): once the writer stopped,
            // nothing emptied the queue - every later pass was serialised
            // and held in memory (up to the queue limit) for the rest of the
            // night. Closing it makes the sink drop lines straight away, and
            // what was already waiting is let go.
            try
            {
                pending.CompleteAdding();

                while (pending.TryTake(out _))
                {
                }
            }
            catch
            {
            }
        }
    }

    private static void DeleteOld(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "passes-*.jsonl"))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > Retention)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // In use or read-only - left for next time.
                }
            }
        }
        catch
        {
        }
    }

    private static string SafeAppVersion()
    {
        try
        {
            return TimingHooks.AppVersionShort();
        }
        catch
        {
            return string.Empty;
        }
    }
}
