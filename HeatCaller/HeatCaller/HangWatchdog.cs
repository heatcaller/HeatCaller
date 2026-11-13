using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HeatCaller;

/// <summary>
/// Notices when the window stops answering, says so in the log, and saves
/// one dump of the frozen app so the cause can be found.
///
/// Added after rehearsal 2 (27 Sep 2026) froze mid-race with no error and
/// nothing on screen to say why. A freeze leaves no crash log - nothing
/// threw - and once the app is force-closed, the only evidence of where it
/// was stuck is gone. A dump taken WHILE it is frozen shows every thread's
/// stack at that moment, the UI thread's included: exactly what it was
/// waiting on or looping in.
///
/// How it tells: once a second a background thread posts a tiny piece of
/// work to the window. A healthy window runs it within milliseconds - a
/// modal dialog, a menu and a window being dragged all still run it, since
/// they keep processing posted messages. Ten seconds without it running is
/// a freeze.
///
/// The dump is written by a SECOND copy of HeatCaller started just for that
/// (see RunDumpHelper), not by this process. Windows' dump writer, run
/// inside the process it is dumping, can deadlock against the threads it
/// suspends - which would turn a ten-second stall into a permanent one.
/// From outside, the worst case is a helper that fails.
///
/// One dump per run, at most two kept, and none when there is under 3 GB
/// free: a dump of this app runs to a few hundred MB.
///
/// A dump is a copy of the app's memory, so it holds the club's keys and
/// PINs and every driver's details in cleartext - see DumpHoldsSecrets,
/// which is said every time one is offered. The dump itself is left exactly
/// as it was: without the managed heap it cannot show what the stuck thread
/// was doing, and finding the freeze is what it is for.
/// </summary>
internal static class HangWatchdog
{
    /// <summary>How long the window can go without answering before it counts as frozen.</summary>
    private static readonly TimeSpan FrozenAfter = TimeSpan.FromSeconds(10);

    /// <summary>How often a freeze that goes on is reported again, with the CPU reading then.</summary>
    private static readonly TimeSpan StillFrozenEvery = TimeSpan.FromSeconds(30);

    /// <summary>The longest the helper is given to write the dump.</summary>
    private static readonly TimeSpan DumpTimeout = TimeSpan.FromMinutes(2);

    private const int KeepDumps = 2;

    private const long MinFreeBytesForDump = 3L * 1024 * 1024 * 1024;

    /// <summary>The switch that makes HeatCaller.exe the dump helper instead of the app.</summary>
    private const string DumpHelperSwitch = "--write-freeze-dump";

    /// <summary>
    /// Said in the log beside the dump's path, every time the dump is
    /// offered.
    ///
    /// The dump is the whole managed heap (see NativeMethods.HeapDump), so
    /// everything the app had unsealed into memory is in it in cleartext:
    /// the relay token, the manager, check-in and racer PINs, the SMTP and
    /// backup passwords, the ElevenLabs key, the badge key behind every
    /// printed check-in QR code, and every driver's name, email and contact.
    /// settings.json is sealed to this Windows account and a support bundle
    /// is redacted first (see Redactor); the dump is neither, and is fully
    /// usable on any PC it is copied to.
    ///
    /// The log used to say only "please keep it: it shows exactly where the
    /// app is stuck", so an operator mailing it on to whoever asked for it
    /// had no way of knowing any of that.
    /// </summary>
    private const string DumpHoldsSecrets =
        "It is a copy of HeatCaller's memory, so it holds the club's keys and PINs and every driver's "
        + "name and contact details in clear - unlike settings.json, which is locked to this PC. Send it "
        + "only to someone trusted with those, and delete it once the freeze has been looked at.";

    private static int _started;

    /// <summary>Set when the app starts closing - see Stop.</summary>
    private static volatile bool _stopped;

    /// <summary>Environment.TickCount64 when the window last ran a ping.</summary>
    private static long _lastAnswerTick;

    /// <summary>1 while a ping is posted and not yet run - one at a time, so a frozen window does not pile them up.</summary>
    private static int _pingOutstanding;

    /// <summary>
    /// Starts watching <paramref name="window"/>. Call once, on the UI
    /// thread, once the window has a handle. Later calls do nothing.
    /// </summary>
    /// <param name="log">Where the watchdog's lines go - the console, which also writes them to the session log from this thread (see SessionLog).</param>
    public static void Start(Control window, Action<string> log)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _lastAnswerTick, Environment.TickCount64);

        var thread = new Thread(() => Run(window, log))
        {
            IsBackground = true,
            Name = "HeatCaller freeze watchdog",
        };

        thread.Start();
    }

    /// <summary>
    /// Stops watching. Called as the app starts to close: the window stops
    /// answering while it shuts down, and that must not be mistaken for a
    /// freeze and dumped.
    /// </summary>
    public static void Stop() => _stopped = true;

    private static void Run(Control window, Action<string> log)
    {
        try
        {
            Watch(window, log);
        }
        catch (Exception ex)
        {
            Say(log, $"[watchdog] Stopped watching for freezes - {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Watch(Control window, Action<string> log)
    {
        using var process = Process.GetCurrentProcess();

        var cpuSamples = new Queue<(long Tick, TimeSpan Cpu)>();
        var lastLoop = Environment.TickCount64;

        // Tick of the last answer before the freeze, while one is being reported.
        long? frozenSince = null;
        long nextStillFrozenReport = 0;
        var dumped = false;

        while (true)
        {
            Thread.Sleep(1000);

            if (_stopped || window.IsDisposed)
            {
                return;
            }

            var now = Environment.TickCount64;

            // Far longer than the one-second sleep: the PC was asleep, or
            // this whole process was paused. Time passed, but nothing froze.
            if (now - lastLoop > 5000 && frozenSince is null)
            {
                Volatile.Write(ref _lastAnswerTick, now);
                cpuSamples.Clear();
            }

            lastLoop = now;

            process.Refresh();
            cpuSamples.Enqueue((now, process.TotalProcessorTime));

            while (cpuSamples.Count > 12)
            {
                cpuSamples.Dequeue();
            }

            if (Interlocked.CompareExchange(ref _pingOutstanding, 1, 0) == 0)
            {
                try
                {
                    window.BeginInvoke(new Action(Answer));
                }
                catch (InvalidOperationException)
                {
                    // No handle to post to. Closing - or the window is
                    // between handles while it is rebuilt, which is not a
                    // freeze: counted as an answer and tried again.
                    if (_stopped || window.IsDisposed)
                    {
                        return;
                    }

                    Volatile.Write(ref _lastAnswerTick, now);
                    Volatile.Write(ref _pingOutstanding, 0);
                    continue;
                }
            }

            var lastAnswer = Volatile.Read(ref _lastAnswerTick);
            var silentMs = now - lastAnswer;

            if (frozenSince is null)
            {
                if (silentMs < FrozenAfter.TotalMilliseconds)
                {
                    continue;
                }

                frozenSince = lastAnswer;
                nextStillFrozenReport = now + (long)StillFrozenEvery.TotalMilliseconds;

                Say(log,
                    $"[watchdog] FROZEN - the window has not answered for {silentMs / 1000.0:0}s "
                    + $"(it last answered at {WallClock(lastAnswer, now)}). {DescribeCpu(cpuSamples)} "
                    + "Lines logged after this come from the app's other threads, which are still running.");

                if (!dumped)
                {
                    dumped = true;
                    WriteDump(log);
                }
            }
            else if (lastAnswer > frozenSince.Value)
            {
                Say(log, $"[watchdog] The window is answering again, after about {(lastAnswer - frozenSince.Value) / 1000.0:0}s frozen.");
                frozenSince = null;
            }
            else if (now >= nextStillFrozenReport)
            {
                nextStillFrozenReport = now + (long)StillFrozenEvery.TotalMilliseconds;
                Say(log, $"[watchdog] Still frozen - {silentMs / 1000.0:0}s now. {DescribeCpu(cpuSamples)}");
            }
        }
    }

    /// <summary>The ping. Runs on the UI thread - so it only runs when the UI thread is free to.</summary>
    private static void Answer()
    {
        Volatile.Write(ref _lastAnswerTick, Environment.TickCount64);
        Volatile.Write(ref _pingOutstanding, 0);
    }

    /// <summary>
    /// How busy the app has been over the last few seconds. The single most
    /// useful fact about a freeze: near a whole core means a thread is stuck
    /// in a loop; near nothing means a thread is waiting for something that
    /// is never going to happen.
    /// </summary>
    private static string DescribeCpu(Queue<(long Tick, TimeSpan Cpu)> samples)
    {
        if (samples.Count < 2)
        {
            return string.Empty;
        }

        var first = samples.Peek();
        var last = samples.Last();
        var wallMs = last.Tick - first.Tick;

        if (wallMs <= 0)
        {
            return string.Empty;
        }

        var ofOneCore = (last.Cpu - first.Cpu).TotalMilliseconds / wallMs * 100.0;

        return $"Over the last {wallMs / 1000.0:0}s the app used {ofOneCore:0}% of one CPU core "
               + $"({Environment.ProcessorCount} cores in this PC) - near 100% means stuck in a loop, near 0% means stuck waiting.";
    }

    private static string WallClock(long tick, long now) =>
        AppTime.TimeWithSeconds(DateTime.Now - TimeSpan.FromMilliseconds(Math.Max(0, now - tick)));

    private static void Say(Action<string> log, string text)
    {
        try
        {
            log($"{AppTime.LogStampNow()}  {text}");
        }
        catch
        {
            // Straight to the file if the console route itself fails.
            SessionLog.Write(text);
        }
    }

    /// <summary>Starts the helper that dumps this process, and waits for it.</summary>
    private static void WriteDump(Action<string> log)
    {
        if (Debugger.IsAttached)
        {
            Say(log, "[watchdog] No dump saved - a debugger is attached, and it can show where the app is stuck itself.");
            return;
        }

        try
        {
            var folder = SessionLog.Folder;
            Directory.CreateDirectory(folder);

            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (root is not null)
            {
                var free = new DriveInfo(root).AvailableFreeSpace;
                if (free < MinFreeBytesForDump)
                {
                    Say(log, $"[watchdog] No dump saved - only {free / 1024.0 / 1024 / 1024:0.0} GB free on {root}, and a dump needs a few hundred MB.");
                    return;
                }
            }

            SessionLog.DeleteOldest(folder, "freeze-*.dmp", KeepDumps - 1);

            var path = Path.Combine(folder, $"freeze-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.dmp");

            var start = HelperStartInfo(Environment.ProcessId, path);
            if (start is null)
            {
                Say(log, "[watchdog] No dump saved - could not work out how to start a second copy of HeatCaller to write it.");
                return;
            }

            Say(log, $"[watchdog] Saving a dump of the frozen app to {path}. It takes a few seconds and runs to a few hundred MB - please keep it: it shows exactly where the app is stuck. {DumpHoldsSecrets}");

            var timer = Stopwatch.StartNew();
            using var helper = Process.Start(start);

            if (helper is null)
            {
                Say(log, "[watchdog] No dump saved - the helper did not start.");
                return;
            }

            if (!helper.WaitForExit((int)DumpTimeout.TotalMilliseconds))
            {
                Say(log, $"[watchdog] The dump helper is still going after {DumpTimeout.TotalSeconds:0}s - left to finish on its own. Check {path} later. {DumpHoldsSecrets}");
                return;
            }

            if (helper.ExitCode == 0 && File.Exists(path))
            {
                var mb = new FileInfo(path).Length / 1024.0 / 1024.0;
                Say(log, $"[watchdog] Dump saved in {timer.Elapsed.TotalSeconds:0.0}s ({mb:0} MB): {path}. {DumpHoldsSecrets}");
            }
            else
            {
                Say(log, $"[watchdog] The dump could not be saved (helper exit code 0x{helper.ExitCode:X8}).");
            }
        }
        catch (Exception ex)
        {
            Say(log, $"[watchdog] The dump could not be saved - {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// A second copy of this program, told to dump <paramref name="processId"/>.
    /// Normally HeatCaller.exe itself (what `dotnet run` starts); when the app
    /// was started as `dotnet HeatCaller.dll`, the same again with the dll.
    /// </summary>
    private static ProcessStartInfo? HelperStartInfo(int processId, string dumpPath)
    {
        var host = Environment.ProcessPath;
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dll = typeof(HangWatchdog).Assembly.Location;
            if (string.IsNullOrEmpty(dll))
            {
                return null;
            }

            start.ArgumentList.Add(dll);
        }

        start.ArgumentList.Add(DumpHelperSwitch);
        start.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(dumpPath);
        return start;
    }

    /// <summary>True when this run is the dump helper rather than the app. Checked first thing in Main.</summary>
    public static bool IsDumpHelper(IReadOnlyList<string> commandLine) =>
        commandLine.Any(a => string.Equals(a, DumpHelperSwitch, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The helper itself: dumps the process named on the command line to the
    /// path named after it, and returns 0, or the error. Touches nothing else -
    /// no window, no settings, no data.
    /// </summary>
    public static int RunDumpHelper(IReadOnlyList<string> commandLine)
    {
        try
        {
            var at = commandLine.ToList().FindIndex(a => string.Equals(a, DumpHelperSwitch, StringComparison.OrdinalIgnoreCase));

            if (at < 0 || at + 2 >= commandLine.Count
                || !int.TryParse(commandLine[at + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var processId))
            {
                return 2;
            }

            var path = commandLine[at + 2];

            using var target = Process.GetProcessById(processId);
            using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            if (NativeMethods.MiniDumpWriteDump(target.Handle, (uint)processId, file.SafeFileHandle, NativeMethods.HeapDump,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
            {
                return 0;
            }

            var error = Marshal.GetLastWin32Error();
            return error != 0 ? error : 1;
        }
        catch (Exception ex)
        {
            return ex.HResult != 0 ? ex.HResult : 1;
        }
    }

    private static class NativeMethods
    {
        // The "heap" dump the .NET tools make: every thread's stack plus the
        // managed heap, which is what Visual Studio needs to show the .NET
        // call stacks - without the loaded program files a full dump adds.
        //
        // The managed heap is also every secret the app has unsealed and
        // every driver's details, in cleartext. Left as it is anyway: a dump
        // without the heap shows thread stacks with no .NET frames and
        // cannot say what the stuck thread was doing, which is the whole
        // reason for taking one. What changed is that the log now says what
        // is in it (DumpHoldsSecrets) instead of only asking for it to be
        // kept.
        private const uint MiniDumpWithDataSegs = 0x00000001;
        private const uint MiniDumpWithHandleData = 0x00000004;
        private const uint MiniDumpWithUnloadedModules = 0x00000020;
        private const uint MiniDumpWithPrivateReadWriteMemory = 0x00000200;
        private const uint MiniDumpWithFullMemoryInfo = 0x00000800;
        private const uint MiniDumpWithThreadInfo = 0x00001000;
        private const uint MiniDumpWithTokenInformation = 0x00040000;

        public const uint HeapDump =
            MiniDumpWithDataSegs | MiniDumpWithHandleData | MiniDumpWithUnloadedModules
            | MiniDumpWithPrivateReadWriteMemory | MiniDumpWithFullMemoryInfo
            | MiniDumpWithThreadInfo | MiniDumpWithTokenInformation;

        [DllImport("dbghelp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            SafeFileHandle hFile,
            uint dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);
    }
}
