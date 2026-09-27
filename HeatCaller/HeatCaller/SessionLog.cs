using System.Collections.Concurrent;
using System.Text;

namespace HeatCaller;

/// <summary>
/// The console, written to a file as it goes - one file per run, in a Logs
/// folder beside the data rather than in it.
///
/// Added after rehearsal 2 (27 Sep 2026) froze mid-race. The console lives
/// only in the window, so force-closing the frozen app took every line
/// leading up to the freeze with it, and there was nothing left to say what
/// had happened. This keeps them.
///
/// Written on a thread of its own, never the one that logged the line: the
/// UI thread must not wait on the disk, and a frozen UI thread must not stop
/// the lines the other threads are still logging from reaching the file -
/// those are the ones that show what was going on around it.
///
/// Not in the data folder (AppPaths.Root) on purpose: backups zip that whole
/// folder, and a night of logs - or a freeze dump (see HangWatchdog) - has
/// no business in a backup. In a rehearsal, the rehearsal's own folder;
/// otherwise %LocalAppData%\HeatCaller\Logs.
/// </summary>
internal static class SessionLog
{
    /// <summary>How many runs' logs are kept. The oldest go when a run starts.</summary>
    private const int KeepLogs = 20;

    /// <summary>
    /// Lines waiting for the writer. Bounded so a writer that has stopped
    /// (a full or failed disk) can never grow memory without limit; past it,
    /// lines are dropped rather than the logging thread made to wait.
    /// </summary>
    private static readonly BlockingCollection<string> Pending = new(boundedCapacity: 50_000);

    private static volatile bool _running;

    /// <summary>The thread Start was called on - the UI thread - so its lines can be told apart in the file.</summary>
    private static int _uiThreadId = -1;

    /// <summary>Where this run's logs and any freeze dump go.</summary>
    public static string Folder => AppPaths.IsRehearsal
        ? Path.Combine(Rehearsal.Folder, "Logs")
        : Path.Combine(AppPaths.LocalRoot, "Logs");

    /// <summary>This run's log file, or null when logging to a file could not start.</summary>
    public static string? FilePath { get; private set; }

    /// <summary>
    /// Opens this run's log file and starts the writer. Call once, on the UI
    /// thread, after the rehearsal switch has been applied (it decides the
    /// folder) and before the main window is built. Never throws: an app
    /// that cannot write its log still runs.
    /// </summary>
    public static void Start()
    {
        if (_running)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            DeleteOldest(Folder, "console-*.log", KeepLogs - 1);

            var path = Path.Combine(Folder, $"console-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

            // Opened here, not on the writer thread, so a file that cannot
            // be created is known about now. FileShare.ReadWrite so the log
            // can be opened in Notepad while the app is still writing it.
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            FilePath = path;
            _uiThreadId = Environment.CurrentManagedThreadId;
            _running = true;

            var thread = new Thread(() => WriteLoop(writer))
            {
                IsBackground = true,
                Name = "HeatCaller session log",
            };

            thread.Start();

            Write($"[log] HeatCaller {AppVersion.Short} started{(AppPaths.IsRehearsal ? " (rehearsal)" : string.Empty)} - "
                  + $"this file is {path}. Each line starts with the time it was logged and the thread that logged it (UI = the window's own thread).");
        }
        catch
        {
            // No log file this run. The console still works.
            _running = false;
            FilePath = null;
        }
    }

    /// <summary>
    /// Adds a line to the file. Safe from any thread, never waits, never
    /// throws. Stamped with the time to the millisecond and the thread that
    /// logged it - during a freeze, which threads are still logging, and
    /// when the UI thread stopped, is most of the story.
    /// </summary>
    public static void Write(string line)
    {
        if (!_running)
        {
            return;
        }

        try
        {
            var thread = Environment.CurrentManagedThreadId;
            var who = thread == _uiThreadId ? "UI " : $"T{thread,-2}";

            Pending.TryAdd($"{DateTime.Now:HH:mm:ss.fff} {who} {line}");
        }
        catch
        {
            // Logging must never be the thing that breaks.
        }
    }

    private static void WriteLoop(StreamWriter writer)
    {
        try
        {
            using (writer)
            {
                foreach (var line in Pending.GetConsumingEnumerable())
                {
                    writer.WriteLine(line);

                    // Flushed whenever the queue runs dry, which during a
                    // race is after almost every line. A flushed line is in
                    // Windows' hands and survives the app being killed -
                    // which is the whole point of this file.
                    if (Pending.Count == 0)
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
            _running = false;
        }
    }

    /// <summary>
    /// Deletes all but the newest <paramref name="keep"/> files matching the
    /// pattern - only ever this app's own logs and dumps, in its own Logs
    /// folder. Best effort: a file that is open elsewhere is left.
    /// </summary>
    internal static void DeleteOldest(string folder, string pattern, int keep)
    {
        try
        {
            var old = new DirectoryInfo(folder)
                .GetFiles(pattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Math.Max(0, keep));

            foreach (var file in old)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
