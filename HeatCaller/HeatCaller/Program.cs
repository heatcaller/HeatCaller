using System.Text;

namespace HeatCaller;

internal static class Program
{
    private static string CrashLogPath => Path.Combine(
        AppPaths.Root,
        "crash.log");

    private static string CrashLogOldPath => Path.Combine(
        Path.GetDirectoryName(CrashLogPath)!,
        "crash.old.log");

    /// <summary>Both crash logs, newest first, for Save Support Info (SupportBundle). Either may not exist.</summary>
    internal static IReadOnlyList<string> CrashLogPaths => new[] { CrashLogPath, CrashLogOldPath };

    // crash.log is appended to forever; past this it's moved aside to
    // crash.old.log and started again, so a fault that repeats all night
    // can't fill the disk.
    private const long CrashLogMaxBytes = 5 * 1024 * 1024;

    // The same error shown again within this long is logged but not shown.
    private static readonly TimeSpan CrashDialogRepeatWindow = TimeSpan.FromSeconds(30);

    private static readonly object CrashGate = new();
    private static readonly Dictionary<string, DateTime> CrashLastShown = new();
    private static int _crashDialogOpen;

    // Held for the life of the process. A static field rather than a local
    // so it can't be collected - and a collected mutex is a released one.
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main()
    {
        // HeatCaller.exe --write-freeze-dump <process> <file>: not the app at
        // all, but the helper the freeze watchdog starts to dump a frozen
        // copy of it from outside (see HangWatchdog). First, before the
        // single-instance check - which would wait on, and then refuse, the
        // very copy it has been asked to dump.
        var commandLine = Environment.GetCommandLineArgs();
        if (HangWatchdog.IsDumpHelper(commandLine))
        {
            Environment.ExitCode = HangWatchdog.RunDumpHelper(commandLine);
            return;
        }

        // Without these, any exception thrown while the app is starting up
        // kills the process with no window, no message, and nothing printed
        // to the console - it just returns to the prompt as if nothing
        // happened, which is impossible to diagnose.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportCrash(e.ExceptionObject as Exception, "Unhandled error");

        Application.ThreadException += (_, e) =>
            ReportCrash(e.Exception, "Unexpected error");

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // A task that faults with nobody awaiting it is otherwise silent -
        // logged here so it can at least be found afterwards. Not shown:
        // it surfaces at some random later garbage collection, long after
        // whatever caused it, and a dialog then would only confuse.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppendCrashLog(e.Exception, "Unobserved background error");
            e.SetObserved();
        };

        // One copy at a time. Two copies share every settings file in
        // %AppData%, and each saves its own in-memory view over the other's
        // - drivers added in one window silently vanish when the other
        // closes. Both would also fight over the decoder's serial port.
        if (!AcquireSingleInstance())
        {
            MessageBox.Show(
                "HeatCaller is already running.\r\n\r\n" +
                "Look for it on the taskbar - only one copy can run at a time.",
                "HeatCaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();

            // HeatCaller.exe --rehearsal: the rehearsal copy - the club's data
            // copied once into a folder of its own, for practising a race night
            // without touching the real one. After the single-instance check,
            // so the club's data is never copied while the real app is writing
            // it. See Rehearsal.
            if (Rehearsal.Wanted(Environment.GetCommandLineArgs()))
            {
                if (!Rehearsal.Start(out var problem))
                {
                    // Never carries on with the real data instead: somebody
                    // who asked for a rehearsal is about to do things they
                    // would not do to the club's results.
                    MessageBox.Show(
                        "The rehearsal copy couldn't be set up, so HeatCaller hasn't started.\r\n\r\n" + problem,
                        "HeatCaller rehearsal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // Before anything reads a file: data saved under the app's
            // earlier names is moved into HeatCaller's folders, once. Not
            // in a rehearsal - it moves the REAL folders, and would move
            // them into the rehearsal's.
            if (!AppPaths.IsRehearsal && !AppDataMigration.Run())
            {
                return;
            }

            // The console, also written to a file as it goes - so a freeze
            // that ends in a force-close still leaves the lines that led up
            // to it. After the rehearsal switch (it decides the folder) and
            // the migration (it moves the folders this writes into).
            SessionLog.Start();

            // The startup screen goes up FIRST, before anything else is
            // built.
            //
            // The main window constructs thirty-odd pages up front, which
            // takes a noticeable moment, and until it can paint, Windows
            // shows an empty white rectangle - the wrong colour for every
            // theme in this app and the first thing anyone sees.
            //
            // Shown here rather than from inside MainForm because by the
            // time MainForm's constructor runs it is already too late: the
            // constructor IS the slow part, and nothing can paint until it
            // returns.
            var splash = new SplashForm();
            splash.Show();
            splash.Update();

            MainForm main;

            try
            {
                splash.Say("Loading settings and driver profiles...");
                main = new MainForm();

                splash.Say("Drawing the first screen...");

                // Held until the main window has actually painted.
                //
                // Not Shown: that fires when the window has been made
                // visible, which is before its controls have drawn
                // themselves. Closing the startup screen there put the
                // white frame straight back - and worse, put it where the
                // startup screen was not covering, which is why it showed
                // up as the sidebar and the strip along the top.
                //
                // Ready fires once the window has gone quiet and revealed
                // itself. See MainForm.RaiseReady.
                main.Ready += (_, _) =>
                {
                    splash.Close();
                    splash.Dispose();

                    // The startup screen was TopMost and took activation on
                    // its way up. Closing it hands focus back to whatever
                    // Windows feels like, which is not reliably this app -
                    // and an app that opens behind the window you launched
                    // it from looks like it failed to start.
                    main.Activate();
                };
            }
            catch
            {
                // A splash left on screen over a crash dialog is its own
                // small disaster.
                splash.Close();
                splash.Dispose();
                throw;
            }

            Application.Run(main);
        }
        catch (Exception ex)
        {
            ReportCrash(ex, "HeatCaller couldn't start");
        }

        // Held until the very end - see _singleInstance.
        GC.KeepAlive(_singleInstance);
    }

    private static bool AcquireSingleInstance()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: false, @"Local\HeatCaller");
            bool acquired;

            try
            {
                // Waits rather than giving up at once: a restart (after a
                // restore, or a theme switch) starts the new copy while the
                // old one is still closing, and it would otherwise be turned
                // away by the very copy that launched it.
                acquired = mutex.WaitOne(TimeSpan.Zero) || mutex.WaitOne(TimeSpan.FromSeconds(8));
            }
            catch (AbandonedMutexException)
            {
                // The previous copy ended without letting go - killed, or
                // Environment.Exit. The mutex is ours now all the same.
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return false;
            }

            _singleInstance = mutex;
            return true;
        }
        catch
        {
            // If the check itself can't be made, run anyway: a second copy
            // is a nuisance, but not starting at all is worse.
            return true;
        }
    }

    private static void AppendCrashLog(Exception ex, string title)
    {
        var details = new StringBuilder();
        details.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {title}");
        details.AppendLine(ex.ToString());
        details.AppendLine(new string('-', 60));

        try
        {
            lock (CrashGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);

                var existing = new FileInfo(CrashLogPath);
                if (existing.Exists && existing.Length > CrashLogMaxBytes)
                {
                    File.Move(CrashLogPath, CrashLogOldPath, overwrite: true);
                }

                File.AppendAllText(CrashLogPath, details.ToString());
            }
        }
        catch
        {
            // If even logging fails, the caller still tries to show a dialog.
        }
    }

    private static void ReportCrash(Exception? ex, string title)
    {
        if (ex is null)
        {
            return;
        }

        // Write full detail to a log first - a message box truncates long
        // stack traces, and the file means the error survives after the
        // dialog is dismissed.
        AppendCrashLog(ex, title);

        // A timer that throws on every tick used to open a fresh dialog per
        // tick, each one modal on top of the last, faster than anyone could
        // close them. Now: one dialog at a time, and the same error shown
        // at most once every thirty seconds. Every occurrence is still in
        // the log.
        var key = $"{ex.GetType().FullName}|{ex.Message}";
        lock (CrashGate)
        {
            var now = DateTime.UtcNow;
            if (CrashLastShown.TryGetValue(key, out var last) && now - last < CrashDialogRepeatWindow)
            {
                return;
            }

            if (CrashLastShown.Count > 100)
            {
                CrashLastShown.Clear();
            }

            CrashLastShown[key] = now;
        }

        if (Interlocked.CompareExchange(ref _crashDialogOpen, 1, 0) != 0)
        {
            return;
        }

        try
        {
            // The real MessageBox, deliberately, and the only one left in
            // the app.
            //
            // Everything else uses AppDialog, which is themed - but AppDialog
            // builds itself out of AppTheme's fonts and colours, and this is
            // the handler that runs when something has already gone wrong.
            // If the failure being reported IS the theme, a themed dialog is
            // the one thing that cannot be trusted to open. The system box
            // has no such dependency.
            MessageBox.Show(
                $"{ex.GetType().Name}: {ex.Message}\r\n\r\n" +
                $"Full details were written to:\r\n{CrashLogPath}\r\n\r\n" +
                "To send them for help: Backup & Restore page, Save Support Info.\r\n\r\n" +
                $"{ex.StackTrace}",
                title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // Nothing more we can do.
        }
        finally
        {
            Interlocked.Exchange(ref _crashDialogOpen, 0);
        }
    }
}
