using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// The "Save Support Info" file: one zip a club can attach to an email when
/// something went wrong, holding what whoever helps them needs to see - the
/// console from the last few runs, the crash log, the version, this PC and
/// the settings that matter - and none of the club's private details.
/// </summary>
/// <remarks>
/// Beta readiness 2 in ROADMAP.md ("a night you can support from somewhere
/// else"). The console has been written to a file since 27 Sep 2026
/// (SessionLog); this is what gets it off the laptop.
///
/// WHAT IS KEPT OUT, and how:
///
///  - Keys, tokens, PINs and passwords: never read at all. The settings
///    summary is an allow-list (DescribeSettings) and says only whether a
///    secret is set. None of them is written to the console either.
///  - Driver names, emails and contact details: replaced line by line in
///    everything that goes in (Redactor). Driver names come from Driver
///    Profiles; a name that isn't there - a turned-down online sign-up, say
///    - can't be recognised, and the README inside says so.
///  - The Windows user name: HeatCaller logs file paths, and every one
///    under C:\Users\ carries it.
///  - Freeze dumps (HangWatchdog): listed but never included. A dump is a
///    copy of the app's memory - everything it had open, keys and names
///    included - and can be gigabytes.
///
/// Built on a background thread from Inputs gathered on the UI thread first
/// (see SupportInfo), because a night's logs are tens of megabytes and the
/// laptop may be timing a race.
/// </remarks>
internal static class SupportBundle
{
    /// <summary>The most runs' logs put in. A race night with a restart or two is three or four.</summary>
    internal const int MaxLogs = 5;

    /// <summary>How far back a log can be and still go in. The newest always goes in, however old.</summary>
    internal static readonly TimeSpan LogWindow = TimeSpan.FromDays(3);

    /// <summary>
    /// Past this much log text the older runs are left out - the newest
    /// always goes in. A four-hour night with the EasyLap is roughly 15MB,
    /// and zipped, text is about a tenth of that - so with the I-Lap
    /// captures below, a full file stays under the 20MB or so most email
    /// takes. SupportInfo says so when one doesn't.
    /// </summary>
    internal const long MaxLogBytes = 100L * 1024 * 1024;

    /// <summary>
    /// The I-Lap's raw capture (ilap-raw-YYYY-MM-DD.log in the data folder,
    /// one file a day, up to 20MB each): the last few days' go in, because
    /// the I-Lap has never been on a real unit and its first problems will
    /// be in what it sent. Only recent ones - an EasyLap club has none.
    /// </summary>
    internal const int MaxDecoderLogs = 3;

    internal const long MaxDecoderLogBytes = 40L * 1024 * 1024;

    /// <summary>The I-Lap's capture, and the Beta decoders' (decoder-raw-NAME-YYYY-MM-DD.log) - up to MaxDecoderLogs of each.</summary>
    internal static readonly string[] DecoderLogPatterns = { "ilap-raw-*.log", "decoder-raw-*.log" };

    internal sealed class Inputs
    {
        /// <summary>Where the console logs are (SessionLog.Folder).</summary>
        public required string LogFolder { get; init; }

        /// <summary>This run's own log, to be marked as such in the README. Null when logging to a file didn't start.</summary>
        public string? CurrentLogPath { get; init; }

        /// <summary>crash.log and crash.old.log - those that don't exist are skipped.</summary>
        public required IReadOnlyList<string> CrashLogPaths { get; init; }

        /// <summary>Where the I-Lap's raw captures are (the data folder, AppPaths.Root). Null for none.</summary>
        public string? DecoderLogFolder { get; init; }

        /// <summary>The version, this PC and the settings - made on the UI thread (DescribeSystem, DescribeSettings).</summary>
        public required string Summary { get; init; }

        public required Redactor Redactor { get; init; }

        /// <summary>Local time now - the log window is measured from it, and the README is dated with it.</summary>
        public required DateTime Now { get; init; }
    }

    /// <summary>What went in, for the message the club sees afterwards.</summary>
    internal sealed record Outcome(
        IReadOnlyList<string> Logs,
        IReadOnlyList<string> LogsLeftOut,
        IReadOnlyList<string> CrashLogs,
        IReadOnlyList<string> DecoderLogs,
        IReadOnlyList<string> DumpsLeftOut,
        IReadOnlyList<string> Unreadable,
        int LinesChanged);

    /// <summary>
    /// Writes the zip to <paramref name="zipPath"/>. Written beside it
    /// under another name first and moved into place at the end, so a
    /// failure part way through never leaves half a file where the club
    /// asked for one. Throws on failure; the caller says what went wrong.
    /// </summary>
    internal static Outcome Write(Inputs inputs, string zipPath)
    {
        var (logs, logsLeftOut) = PickLogs(inputs.LogFolder, inputs.Now);
        var crashLogs = inputs.CrashLogPaths.Where(File.Exists).ToList();
        // The Beta decoders' captures too (decoder-raw-*.log, 2 Nov 2026): the
        // console asks for them with every report, and they were left out.
        var decoderLogs = inputs.DecoderLogFolder is { } decoderFolder
            ? DecoderLogPatterns
                .SelectMany(pattern => PickRecent(decoderFolder, pattern, inputs.Now, MaxDecoderLogs, MaxDecoderLogBytes, alwaysNewest: false).Picked)
                .ToList()
            : new List<FileInfo>();
        var dumps = FilesIn(inputs.LogFolder, "freeze-*.dmp").OrderByDescending(f => f.LastWriteTimeUtc).ToList();

        var partPath = zipPath + ".part";
        var linesChanged = 0;

        // A file that can't be OPENED is left out and named in the README,
        // rather than costing the club the whole file - crash.log can be
        // moved aside to crash.old.log between being found and being read,
        // or another program can have it locked. Only the opening is caught:
        // a failure writing the zip itself (a full disk) is a failed save,
        // and is reported as one.
        var unreadable = new List<string>();

        bool CopyOrNote(ZipArchive archive, string entryName, string sourcePath)
        {
            FileStream source;

            try
            {
                // Sharing read, write and delete: this run's own log is still
                // open for writing (SessionLog), and would otherwise refuse.
                source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable.Add($"{Path.GetFileName(sourcePath)} ({ex.Message})");
                return false;
            }

            using (source)
            {
                linesChanged += CopyRedacted(archive, entryName, source, inputs.Redactor);
            }

            return true;
        }

        // What actually went in - the README, the dialog and the console
        // line all say these, not what was meant to.
        var logsIn = new List<FileInfo>();
        var crashLogsIn = new List<string>();
        var decoderLogsIn = new List<FileInfo>();

        try
        {
            using (var file = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                linesChanged += WriteText(zip, "summary.txt", inputs.Summary, inputs.Redactor);

                foreach (var log in logs)
                {
                    if (CopyOrNote(zip, "logs/" + log.Name, log.FullName))
                    {
                        logsIn.Add(log);
                    }
                }

                foreach (var crash in crashLogs)
                {
                    if (CopyOrNote(zip, "crash/" + Path.GetFileName(crash), crash))
                    {
                        crashLogsIn.Add(crash);
                    }
                }

                foreach (var decoderLog in decoderLogs)
                {
                    if (CopyOrNote(zip, "decoder/" + decoderLog.Name, decoderLog.FullName))
                    {
                        decoderLogsIn.Add(decoderLog);
                    }
                }

                // Last, because it reports the count of lines changed in
                // everything above it. Put through the redactor like the
                // rest - it names folders, and folders carry the user name.
                var readme = Readme(inputs, logsIn, logsLeftOut, crashLogsIn, decoderLogsIn, dumps, unreadable, linesChanged);
                WriteText(zip, "README.txt", readme, inputs.Redactor);
            }

            File.Move(partPath, zipPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(partPath);
            }
            catch
            {
                // Left for the club to delete - it is named .part, and
                // nothing reads it.
            }

            throw;
        }

        return new Outcome(
            logsIn.Select(l => l.Name).ToList(),
            logsLeftOut.Select(l => l.Name).ToList(),
            crashLogsIn.Select(p => Path.GetFileName(p) ?? p).ToList(),
            decoderLogsIn.Select(d => d.Name).ToList(),
            dumps.Select(d => d.Name).ToList(),
            unreadable,
            linesChanged);
    }

    /// <summary>
    /// The newest log always; then, newest first, those written in the last
    /// <see cref="LogWindow"/>, up to <see cref="MaxLogs"/> and
    /// <see cref="MaxLogBytes"/> between them. Returns what goes in, and the
    /// recent logs left out for size or number (older ones aren't
    /// mentioned - nobody asked about last month).
    /// </summary>
    internal static (List<FileInfo> Picked, List<FileInfo> LeftOut) PickLogs(string folder, DateTime nowLocal) =>
        PickRecent(folder, "console-*.log", nowLocal, MaxLogs, MaxLogBytes, alwaysNewest: true);

    /// <summary>PickLogs for any kind of file: <paramref name="alwaysNewest"/> takes the newest even when it is old.</summary>
    internal static (List<FileInfo> Picked, List<FileInfo> LeftOut) PickRecent(
        string folder, string pattern, DateTime nowLocal, int maxFiles, long maxBytes, bool alwaysNewest)
    {
        // Read again from the file itself: a listing's size and time for a
        // file still open for writing - this run's own log - can be those it
        // had when it was opened, which put this run's log down as 0 KB.
        // Refresh never throws; reading the values afterwards can, for a
        // file deleted or locked away since the listing, and such a file is
        // simply not offered.
        var all = new List<FileInfo>();

        foreach (var file in FilesIn(folder, pattern))
        {
            try
            {
                file.Refresh();

                if (file.Exists)
                {
                    _ = file.Length;
                    _ = file.LastWriteTimeUtc;
                    all.Add(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        all = all.OrderByDescending(f => f.LastWriteTimeUtc).ToList();

        var picked = new List<FileInfo>();
        var leftOut = new List<FileInfo>();
        long bytes = 0;

        foreach (var log in all)
        {
            var recent = log.LastWriteTime >= nowLocal - LogWindow;

            if (picked.Count == 0 && alwaysNewest)
            {
                picked.Add(log);
                bytes += log.Length;
                continue;
            }

            if (!recent)
            {
                continue;
            }

            if (picked.Count < maxFiles && bytes + log.Length <= maxBytes)
            {
                picked.Add(log);
                bytes += log.Length;
            }
            else
            {
                leftOut.Add(log);
            }
        }

        return (picked, leftOut);
    }

    private static IEnumerable<FileInfo> FilesIn(string folder, string pattern)
    {
        try
        {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).GetFiles(pattern, SearchOption.TopDirectoryOnly)
                : Array.Empty<FileInfo>();
        }
        catch
        {
            return Array.Empty<FileInfo>();
        }
    }

    /// <summary>
    /// Copies an open text file into the zip a line at a time, each line
    /// through the redactor. The caller opens it first, so a file that
    /// won't open leaves no empty entry behind. Returns how many lines had
    /// something replaced.
    /// </summary>
    private static int CopyRedacted(ZipArchive zip, string entryName, Stream source, Redactor redactor)
    {
        var changed = 0;

        using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen: true);

        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        while (reader.ReadLine() is { } line)
        {
            writer.Write(redactor.Apply(line, out var lineChanged));
            writer.Write("\r\n");

            if (lineChanged)
            {
                changed++;
            }
        }

        return changed;
    }

    private static int WriteText(ZipArchive zip, string entryName, string text, Redactor redactor)
    {
        var changed = 0;
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);

        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            writer.Write(redactor.Apply(line, out var lineChanged));
            writer.Write("\r\n");

            if (lineChanged)
            {
                changed++;
            }
        }

        return changed;
    }

    private static string Readme(
        Inputs inputs,
        IReadOnlyList<FileInfo> logs,
        IReadOnlyList<FileInfo> logsLeftOut,
        IReadOnlyList<string> crashLogs,
        IReadOnlyList<FileInfo> decoderLogs,
        IReadOnlyList<FileInfo> dumps,
        IReadOnlyList<string> unreadable,
        int linesChanged)
    {
        var text = new StringBuilder();

        text.AppendLine("HeatCaller support information");
        text.AppendLine($"Saved {inputs.Now:yyyy-MM-dd HH:mm} by {AppVersion.Full}.");
        text.AppendLine();
        text.AppendLine("This file is for whoever helps your club with HeatCaller. Inside:");
        text.AppendLine();
        text.AppendLine("  summary.txt  The app's version, this PC, and the settings that help work out");
        text.AppendLine("               a problem. No keys, tokens, PINs or passwords - only whether each");
        text.AppendLine("               one is set.");

        if (logs.Count == 0)
        {
            text.AppendLine($"  (no logs)    No console logs were found in {inputs.LogFolder}.");
        }
        else
        {
            text.AppendLine($"  logs\\        The console from the last {Plural.Of(logs.Count, "run")} of HeatCaller, newest first:");

            foreach (var log in logs)
            {
                var current = inputs.CurrentLogPath is { } path
                              && string.Equals(Path.GetFullPath(path), log.FullName, StringComparison.OrdinalIgnoreCase)
                    ? "  (the run this was saved from)"
                    : string.Empty;

                text.AppendLine($"                 {log.Name}  {log.Length / 1024.0:0} KB{current}");
            }
        }

        if (logsLeftOut.Count > 0)
        {
            text.AppendLine($"               Left out to keep the file small: {string.Join(", ", logsLeftOut.Select(l => l.Name))}.");
        }

        text.AppendLine(crashLogs.Count > 0
            ? "  crash\\       The crash log: every error HeatCaller couldn't deal with, in full."
            : "  (no crash)   There is no crash log - HeatCaller hasn't hit an error it couldn't deal with.");

        if (decoderLogs.Count > 0)
        {
            text.AppendLine($"  decoder\\     Everything the I-Lap decoder sent, one file a day: {string.Join(", ", decoderLogs.Select(d => d.Name))}.");
        }

        if (unreadable.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Couldn't be read, so not included:");

            foreach (var file in unreadable)
            {
                text.AppendLine($"  {file}");
            }
        }

        text.AppendLine();
        text.AppendLine("Before anything went in:");
        text.AppendLine();
        text.AppendLine("  - Every driver's name in Driver Hub was replaced with [driver 1], [driver 2]");
        text.AppendLine("    and so on - the same number for the same driver all the way through, so one");
        text.AppendLine("    driver can still be followed through the night.");
        text.AppendLine("  - Email addresses were replaced with [email], and the contact details in");
        text.AppendLine("    Driver Hub with [contact].");
        text.AppendLine("  - The Windows user name in folder paths was replaced with [user].");
        text.AppendLine($"  {Plural.Of(linesChanged, "line")} had something replaced.");
        text.AppendLine();
        text.AppendLine("Not replaced: a name that isn't in Driver Hub (an online sign-up that was");
        text.AppendLine("turned down, say), and transponder numbers. Open the logs and look if you need");
        text.AppendLine("to be sure.");
        text.AppendLine();

        if (dumps.Count == 0)
        {
            text.AppendLine("Not included: freeze dumps. There are none - HeatCaller hasn't stopped responding.");
        }
        else
        {
            text.AppendLine("Not included: freeze dumps. A freeze dump is a copy of HeatCaller's memory, taken");
            text.AppendLine("when it stopped responding. It holds everything the app had open - names and keys");
            text.AppendLine("included - so it is only sent when it's asked for. This PC has:");

            foreach (var dump in dumps)
            {
                text.AppendLine($"  {dump.Name}  {dump.Length / (1024.0 * 1024.0):0} MB");
            }

            text.AppendLine($"in {inputs.LogFolder}");
        }

        return text.ToString();
    }

    // --- What goes in summary.txt ---------------------------------------------------------

    /// <summary>
    /// The version and this PC. Reads the screens, so call it on the UI
    /// thread. Every part is guarded: a PC that won't say is noted, not a
    /// reason for no file.
    /// </summary>
    internal static string DescribeSystem(DateTime nowLocal)
    {
        var text = new StringBuilder();

        text.AppendLine("HEATCALLER");
        Line(text, "Version", () => AppVersion.Full);
        Line(text, "Saved", () =>
        {
            var offset = TimeZoneInfo.Local.GetUtcOffset(nowLocal);
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            return $"{nowLocal:yyyy-MM-dd HH:mm:ss} (UTC{sign}{offset.Duration():hh\\:mm})";
        });
        Line(text, "Rehearsal", () => AppPaths.IsRehearsal ? "yes" : "no");
        Line(text, "Running since", () =>
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            return $"{process.StartTime:yyyy-MM-dd HH:mm:ss}";
        });

        text.AppendLine();
        text.AppendLine("THIS PC");
        Line(text, "Windows", () => RuntimeInformation.OSDescription);
        Line(text, ".NET", () => RuntimeInformation.FrameworkDescription);
        Line(text, "64-bit", () => Environment.Is64BitProcess ? "yes" : "no");
        Line(text, "Processors", () => Environment.ProcessorCount.ToString());
        Line(text, "Memory", () => $"{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024):0.0} GB");
        Line(text, "Language", () => System.Globalization.CultureInfo.CurrentCulture.Name);
        Line(text, "Screens", () => string.Join("; ", Screen.AllScreens.Select(s =>
            $"{s.Bounds.Width}x{s.Bounds.Height}{(s.Primary ? " (main)" : string.Empty)}")));
        Line(text, "Free space", () =>
        {
            var root = Path.GetPathRoot(AppPaths.Root);
            return root is null ? "unknown" : $"{new DriveInfo(root).AvailableFreeSpace / (1024.0 * 1024 * 1024):0.0} GB on {root}";
        });

        return text.ToString();
    }

    private static void Line(StringBuilder text, string label, Func<string> value)
    {
        string shown;

        try
        {
            shown = value();
        }
        catch (Exception ex)
        {
            shown = $"(couldn't tell: {ex.GetType().Name})";
        }

        text.AppendLine($"  {(label + ":").PadRight(16)}{shown}");
    }

    /// <summary>
    /// The settings that help work out a problem - an allow-list, written
    /// out one by one. NEVER a loop over every property: a setting added
    /// later could be a secret, and would then go out in the next file
    /// without anybody having decided it should. For each secret, only
    /// whether it is set.
    /// </summary>
    /// <param name="elevenLabsKeySet">Whether the voice settings hold an ElevenLabs key (VoiceProfile.ApiKey) - Bug hunt 11, 5 Nov 2026.</param>
    internal static string DescribeSettings(AppSettings s, bool elevenLabsKeySet)
    {
        var text = new StringBuilder();

        static string YesNo(bool value) => value ? "yes" : "no";
        static string IsSet(string? value) => string.IsNullOrEmpty(value) ? "not set" : "set";

        // Worked out first rather than inside the lines below: a switch or a
        // pattern in an interpolation hole is harder to read than it's worth.
        var decoder = DecoderConnection.ShortName(s.LastConnectionType);

        var lastBackup = s.LastAutoBackupUtc is { } last
            ? last.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "never";

        var iLapPort = string.IsNullOrEmpty(s.ILapPort) ? "(none)" : s.ILapPort;

        text.AppendLine("DECODER");
        text.AppendLine($"  Decoder:                 {decoder}");
        text.AppendLine($"  Connect automatically:   {YesNo(s.DirectDecoderAutoConnect)}");
        text.AppendLine($"  I-Lap port:              {iLapPort} at {s.ILapBaud} baud");
        text.AppendLine($"  Split loop:              {(s.SplitLoopEnabled ? $"on, Line Up offset {s.SplitLoopOffsetMs} ms" : "off")}");

        text.AppendLine();
        text.AppendLine("RACES");
        text.AppendLine($"  Lap limit:               {s.RaceLapLimit}");
        text.AppendLine($"  Time limit:              {s.RaceTimeLimitMinutes} min");
        text.AppendLine($"  Minimum lap time:        {s.RaceMinLapTimeMs} ms");
        text.AppendLine($"  Finish on next crossing: {YesNo(s.RaceFinishOnNextCrossing)}");
        text.AppendLine($"  Start type:              {s.RaceStartType}");
        text.AppendLine($"  Stagger:                 {s.RaceStaggerGapSeconds}s, {s.RaceStaggerClock}");
        text.AppendLine($"  Automatic start:         staging {s.AutoStartStagingSeconds}s, countdown {s.AutoStartCountdownSeconds}s, chained {YesNo(s.ChainStartSequence)}");
        text.AppendLine($"  Warm-up:                 {s.RaceWarmUpSeconds}s");
        text.AppendLine($"  Next race automatically: {YesNo(s.AutoAdvanceRounds)} ({s.AutoAdvanceScope})");
        text.AppendLine($"  Result editing:          {YesNo(s.AllowResultEditing)}");

        text.AppendLine();
        text.AppendLine("ANNOUNCER");
        text.AppendLine($"  Muted:                   {YesNo(s.AnnouncerMuted)}");
        text.AppendLine($"  Commentary:              {YesNo(s.CommentaryEnabled)} - local model {YesNo(s.CommentaryUseLocalModel)}, {s.CommentaryTimeoutSeconds}s");
        text.AppendLine($"  Intros / recaps / notes: {YesNo(s.CommentaryRaceIntros)} / {YesNo(s.CommentaryRaceSummaries)} / {YesNo(s.CommentaryTalkingPoints)}");
        text.AppendLine($"  Tone, max length:        {s.CommentaryToneStyle}, {s.CommentaryMaxWords} words");
        text.AppendLine($"  Sound with races:        {YesNo(s.AudioStartWithRaces)} (music {YesNo(s.AudioStartMusic)}, ambience {YesNo(s.AudioStartAmbience)})");
        // 5 Nov 2026, Bug hunt 11: was IsSet(s.ApiKey) - the retired Next
        // Level Timing key - so a club with a working ElevenLabs key read
        // "not set". The key lives in the voice settings; the caller says.
        text.AppendLine($"  ElevenLabs key:          {(elevenLabsKeySet ? "set" : "not set")}");

        text.AppendLine();
        text.AppendLine("ONLINE AND PHONES");
        text.AppendLine($"  Club website (relay):    {YesNo(s.RelayEnabled)} - {s.RelayBaseUrl} club \"{s.RelayClub}\", token {IsSet(s.RelayToken)}");
        text.AppendLine($"  HeatCaller voice relay:  {s.VoiceRelayBaseUrl} club \"{s.VoiceRelayClub}\", token {IsSet(s.VoiceRelayToken)}");
        text.AppendLine($"  Online check-in:         {YesNo(s.RelayCheckInEnabled)}, racer PINs {YesNo(s.RelayRacerPinsEnabled)}");
        text.AppendLine($"  Manager over the relay:  {YesNo(s.ManagerViaRelay)}");
        text.AppendLine($"  Local screens server:    starts automatically {YesNo(s.WebServerAutoStart)}, port {s.WebServerPort}");
        text.AppendLine($"  Keys locked (restored):  {YesNo(s.SecretsLocked)}");

        text.AppendLine();
        text.AppendLine("BACKUPS");
        text.AppendLine($"  Automatic:               {YesNo(s.AutoBackupEnabled)}, every {s.AutoBackupIntervalDays} days, keep {s.AutoBackupMaxToKeep}");
        text.AppendLine($"  Last backup:             {lastBackup}");
        text.AppendLine($"  Cloud copies:            Google Drive {YesNo(s.CloudBackupGoogleDrive)}, iCloud {YesNo(s.CloudBackupICloudDrive)}, OneDrive {YesNo(s.CloudBackupOneDrive)}");
        text.AppendLine($"  Backup password:         {IsSet(s.BackupPassword)}");

        text.AppendLine();
        text.AppendLine("SCREEN");
        text.AppendLine($"  Look:                    {s.AppLook}, dark {YesNo(s.DarkMode)}, text size x{s.FontScale:0.00}");
        text.AppendLine($"  24-hour clock:           {YesNo(s.Use24HourTime)}");
        text.AppendLine($"  Race-night focus:        {YesNo(s.RaceNightFocus)}");
        text.AppendLine($"  Hide untested pages:     {YesNo(s.HideUntestedPages)}");

        return text.ToString();
    }
}
