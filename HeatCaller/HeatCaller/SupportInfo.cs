namespace HeatCaller;

/// <summary>
/// "Save Support Info": asks where, gathers what SupportBundle needs on the
/// UI thread, builds the zip in the background, then says what is in it and
/// shows it in its folder, ready to attach to an email.
/// </summary>
/// <remarks>
/// Reached from Backup &amp; Restore (the Getting Help box) and from the
/// console's right-click menu - the two places somebody with a problem is
/// likely to be looking. The crash dialog points at the first.
/// </remarks>
internal static class SupportInfo
{
    /// <summary>
    /// Past this, the file is said to be too big for most email - Gmail and
    /// Outlook stop at 20-25MB - and to go by a cloud drive link instead.
    /// </summary>
    private const long EmailLimitBytes = 20L * 1024 * 1024;

    /// <summary>1 while one is being built: a second click waits for the first rather than zipping the same logs twice.</summary>
    private static int _saving;

    /// <param name="owner">The window the dialogs belong to.</param>
    /// <param name="log">The console. Called on the UI thread.</param>
    public static async void Save(Control owner, AppSettings settings, ProfileStore profiles, Action<string> log)
    {
        if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0)
        {
            AppDialog.Show(owner, "Support information is already being saved - it will say when it's done.",
                "Already saving", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string? zipPath;
        SupportBundle.Outcome? outcome;

        try
        {
            (zipPath, outcome) = await Build(owner, settings, profiles, log);
        }
        catch (Exception ex)
        {
            // async void: anything that got this far would otherwise end up
            // in the crash dialog, for a button whose whole job is helping.
            TryLog(log, $"{AppTime.LogStampNow()}  [support] Couldn't save support information: {ex.Message}");

            if (!owner.IsDisposed)
            {
                AppDialog.Show(owner, $"The support information couldn't be saved.\n\n{ex.Message}",
                    "Not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return;
        }
        finally
        {
            Interlocked.Exchange(ref _saving, 0);
        }

        if (zipPath is null || outcome is null)
        {
            return; // cancelled at the file dialog
        }

        // Saved. Anything that goes wrong from here is only in telling the
        // club about it - never reported as the file not being saved.
        try
        {
            Report(owner, zipPath, outcome, log);
        }
        catch (Exception ex)
        {
            TryLog(log, $"{AppTime.LogStampNow()}  [support] Saved {zipPath}, but couldn't show it: {ex.Message}");
        }
    }

    /// <summary>Asks where, then builds the file. Returns nulls when the club cancels.</summary>
    private static async Task<(string? Path, SupportBundle.Outcome? Outcome)> Build(
        Control owner, AppSettings settings, ProfileStore profiles, Action<string> log)
    {
        var now = DateTime.Now;

        string zipPath;

        using (var dialog = new SaveFileDialog
        {
            Title = "Save support information",
            Filter = "Zip file (*.zip)|*.zip",
            FileName = $"HeatCaller support {now:yyyy-MM-dd HH-mm}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = "zip",
        })
        {
            if (dialog.ShowDialog(owner) != DialogResult.OK)
            {
                return (null, null);
            }

            zipPath = dialog.FileName;
        }

        // Everything read from the stores and settings is read here, on the
        // UI thread that owns them. Building the name matcher - one pattern
        // holding every form of every driver's name - and reading and
        // zipping the files goes to the background: a big club's roster is
        // a noticeable pause, and a race may be running.
        var people = profiles.All
            .Select(p => new Redactor.Person(
                new[] { p.DriverName, p.AnnouncerName }.Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
                new[] { p.Contact, p.Email }.Where(c => !string.IsNullOrWhiteSpace(c)).ToList()))
            .ToList();

        var summary = SupportBundle.DescribeSystem(now) + Environment.NewLine + SupportBundle.DescribeSettings(settings);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var logFolder = SessionLog.Folder;
        var currentLog = SessionLog.FilePath;
        var crashLogs = Program.CrashLogPaths;
        var dataFolder = AppPaths.Root;

        log($"{AppTime.LogStampNow()}  [support] Saving support information to {zipPath}...");

        var outcome = await Task.Run(() => SupportBundle.Write(new SupportBundle.Inputs
        {
            LogFolder = logFolder,
            CurrentLogPath = currentLog,
            CrashLogPaths = crashLogs,
            DecoderLogFolder = dataFolder,
            Summary = summary,
            Redactor = Redactor.For(people, userProfile),
            Now = now,
        }, zipPath));

        return (zipPath, outcome);
    }

    /// <summary>The console line, the message saying what went in, and the folder opened on the file.</summary>
    private static void Report(Control owner, string zipPath, SupportBundle.Outcome outcome, Action<string> log)
    {
        var size = new FileInfo(zipPath).Length;
        var megabytes = size / (1024.0 * 1024.0);

        log($"{AppTime.LogStampNow()}  [support] Saved {Path.GetFileName(zipPath)} ({megabytes:0.0} MB): {Plural.Of(outcome.Logs.Count, "log")}, "
            + $"{(outcome.CrashLogs.Count > 0 ? "the crash log" : "no crash log")}, {Plural.Of(outcome.LinesChanged, "line")} with names or addresses replaced.");

        if (owner.IsDisposed)
        {
            return;
        }

        var inside = new List<string>
        {
            "- the app's version, this PC, and the settings that help work out a problem (no keys, tokens, PINs or passwords)",
            outcome.Logs.Count > 0
                ? $"- the console from the last {Plural.Of(outcome.Logs.Count, "run")} of HeatCaller"
                : "- no console logs (none were found)",
        };

        if (outcome.CrashLogs.Count > 0)
        {
            inside.Add("- the crash log");
        }

        if (outcome.DecoderLogs.Count > 0)
        {
            inside.Add("- what the decoder sent over the last few days");
        }

        var notes = string.Empty;

        if (outcome.Unreadable.Count > 0)
        {
            var one = outcome.Unreadable.Count == 1;
            notes += $"\n\n{Plural.Of(outcome.Unreadable.Count, "file")} couldn't be read, so {(one ? "it was" : "they were")} left out - the README inside says which.";
        }

        if (outcome.DumpsLeftOut.Count > 0)
        {
            notes += "\n\nFreeze dumps aren't included - they hold everything HeatCaller had open. Send one only if you're asked for it (the README inside says where they are).";
        }

        var send = size > EmailLimitBytes
            ? $"At {megabytes:0} MB it's too big for most email. Put it in Google Drive, OneDrive or Dropbox and send the link instead."
            : "Attach it to an email to whoever helps your club with HeatCaller.";

        AppDialog.Show(owner,
            $"Saved {Path.GetFileName(zipPath)} ({megabytes:0.0} MB). It holds:\n\n{string.Join("\n", inside)}\n\n"
            + "Driver names, email addresses and your Windows user name were replaced before anything went in - the README inside says exactly what was changed."
            + notes
            + $"\n\n{send} Its folder opens next.",
            "Support information saved", MessageBoxButtons.OK, MessageBoxIcon.Information);

        ShowInFolder(zipPath, log);
    }

    /// <summary>Opens Explorer on the file's folder with the file selected. A failure only goes to the console - the file is saved either way.</summary>
    private static void ShowInFolder(string path, Action<string> log)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            TryLog(log, $"{AppTime.LogStampNow()}  [support] Couldn't open the folder: {ex.Message}");
        }
    }

    private static void TryLog(Action<string> log, string line)
    {
        try
        {
            log(line);
        }
        catch
        {
            // The console is the one thing that can't report its own failure.
        }
    }
}
