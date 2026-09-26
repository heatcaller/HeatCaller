using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// Brings a laptop's data across from the folders older versions used
/// (see LegacyNames) to HeatCaller's own, once, before anything else in the
/// app reads a file.
///
///   %AppData%\(old)              ->  %AppData%\HeatCaller
///   Documents\(old) Results      ->  Documents\HeatCaller Results
///   Documents\(old) Backups      ->  Documents\HeatCaller Backups
///
/// A folder is moved whole when the new one isn't there yet - one rename,
/// nothing copied, nothing that can half-happen. If the new one is already
/// there, the old one's files are moved in one at a time; a file both have
/// keeps the new folder's copy in use and the old one beside it as
/// "name (older copy)".
///
/// Then, once per data folder, the saved JSON is brought up to date: full
/// paths that point into an old folder are pointed at the new one, and the
/// voice settings' renamed fields are carried across. A restore clears the
/// marker (see BackupService), so a backup made by an older version is
/// brought up to date the same way on the restart that follows it.
///
/// Backup zips in the backup folders get today's name prefix, so the
/// Backups page keeps listing - and pruning - them.
///
/// Nothing here is allowed to stop the app starting, with one exception:
/// an older copy that is still running. Its files are open and it would go
/// on writing to the old folder after the move, so the app says so and
/// exits instead.
/// </summary>
internal static class AppDataMigration
{
    public const string DataFolderName = "HeatCaller";
    public const string ResultsFolderName = "HeatCaller Results";
    public const string BackupsFolderName = "HeatCaller Backups";

    /// <summary>Written to the data folder once its JSON is up to date.</summary>
    public const string MarkerFileName = "heatcaller-migrated.txt";

    // Settings files are small; anything this big is not one of ours to edit.
    private const long MaxJsonBytes = 64L * 1024 * 1024;

    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static string DataDirectory => Path.Combine(AppData, DataFolderName);

    /// <summary>
    /// Does the whole job. False only when an older copy of the app is
    /// still running - the caller should exit.
    /// </summary>
    public static bool Run()
    {
        if (OlderCopyRunning())
        {
            AppDialog.Show(
                "An older copy of the app is still running.\r\n\r\n" +
                "Close it (check the taskbar and Task Manager), then start HeatCaller again. " +
                "HeatCaller moves your drivers, results and settings into its own folder the first time it starts, " +
                "and can't do that while the older copy has them open.",
                "HeatCaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var problems = new List<string>();

        MoveFolder(Path.Combine(AppData, LegacyNames.DataFolder), DataDirectory, problems);
        MoveFolder(Path.Combine(Documents, LegacyNames.ResultsFolder), Path.Combine(Documents, ResultsFolderName), problems);
        MoveFolder(Path.Combine(Documents, LegacyNames.BackupsFolder), Path.Combine(Documents, BackupsFolderName), problems);

        UpdateSavedFiles(problems);

        RenameBackups(Path.Combine(Documents, BackupsFolderName), problems);
        if (ConfiguredBackupFolder() is { } custom)
        {
            RenameBackups(custom, problems);
        }

        if (problems.Count > 0)
        {
            Log(problems);

            AppDialog.Show(
                "HeatCaller moved your data into its own folders, but not everything could be moved:\r\n\r\n" +
                string.Join("\r\n", problems.Take(8).Select(p => "- " + p)) +
                (problems.Count > 8 ? $"\r\n- ...and {problems.Count - 8} more (see crash.log)" : string.Empty) +
                "\r\n\r\nNothing has been deleted. HeatCaller tries again each time it starts.",
                "HeatCaller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        return true;
    }

    private static bool OlderCopyRunning()
    {
        try
        {
            return Mutex.TryOpenExisting(LegacyNames.InstanceMutex, out var mutex) && Dispose(mutex);
        }
        catch
        {
            return false;
        }

        static bool Dispose(Mutex mutex)
        {
            mutex.Dispose();
            return true;
        }
    }

    private static void MoveFolder(string from, string to, List<string> problems)
    {
        try
        {
            if (!Directory.Exists(from))
            {
                return;
            }

            if (!Directory.Exists(to))
            {
                try
                {
                    Directory.Move(from, to);
                    return;
                }
                catch (IOException)
                {
                    // Something in it is open (a sync client, an Explorer
                    // preview) - fall through and move what can be moved.
                }
                catch (UnauthorizedAccessException)
                {
                }

                Directory.CreateDirectory(to);
            }

            MergeInto(from, to, problems);
        }
        catch (Exception ex)
        {
            problems.Add($"{from}: {ex.Message}");
        }
    }

    /// <summary>Moves every file across that the new folder doesn't already have, then removes whatever is left empty.</summary>
    private static void MergeInto(string from, string to, List<string> problems)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).ToList())
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));

            try
            {
                // Both have it: the new folder's copy stays in use, and the
                // old one is kept beside it under another name rather than
                // left behind or thrown away.
                if (File.Exists(target))
                {
                    target = FreeName(target);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target);
            }
            catch (Exception ex)
            {
                problems.Add($"{file}: {ex.Message}");
            }
        }

        // Deepest first, so a folder is empty by the time it's reached.
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length)
                     .Append(from))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch
            {
                // An empty folder left behind costs nothing.
            }
        }
    }

    /// <summary>"x (older copy).json", or "x (older copy 2).json" and so on when that is taken too.</summary>
    private static string FreeName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(dir, n == 1 ? $"{stem} (older copy){ext}" : $"{stem} (older copy {n}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void UpdateSavedFiles(List<string> problems)
    {
        var dataDir = DataDirectory;
        var marker = Path.Combine(dataDir, MarkerFileName);

        if (!Directory.Exists(dataDir) || File.Exists(marker))
        {
            return;
        }

        var failed = false;

        foreach (var file in Directory.EnumerateFiles(dataDir, "*.json", SearchOption.AllDirectories).ToList())
        {
            try
            {
                if (new FileInfo(file).Length > MaxJsonBytes)
                {
                    continue;
                }

                var before = File.ReadAllText(file);
                var after = UpdateJson(before);

                if (string.Equals(before, after, StringComparison.Ordinal))
                {
                    continue;
                }

                // Written beside and swapped in, so a failure halfway leaves
                // the original untouched.
                var temp = file + ".migrating";
                File.WriteAllText(temp, after, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temp, file, overwrite: true);
            }
            catch (Exception ex)
            {
                failed = true;
                problems.Add($"{file}: {ex.Message}");
            }
        }

        if (!failed)
        {
            try
            {
                File.WriteAllText(marker,
                    "Saved paths and settings were brought up to date for HeatCaller. " +
                    "Delete this file to have that checked again on the next start." + Environment.NewLine);
            }
            catch
            {
                // Checked again next time - harmless.
            }
        }
    }

    /// <summary>The JSON with old folder names in paths, and old voice field names, replaced.</summary>
    internal static string UpdateJson(string json)
    {
        // Longest first: "X Results" and "X Backups" before plain "X".
        var folders = new[]
        {
            (LegacyNames.ResultsFolder, ResultsFolderName),
            (LegacyNames.BackupsFolder, BackupsFolderName),
            (LegacyNames.DataFolder, DataFolderName),
        };

        foreach (var (oldName, newName) in folders)
        {
            // Only as a whole path segment: after a separator (JSON writes a
            // backslash as \\, a forward slash as itself), and followed by
            // another separator or the end of the string.
            var pattern = @"(?<=\\\\|/)" + Regex.Escape(oldName) + @"(?=\\\\|/|"")";
            json = Regex.Replace(json, pattern, newName, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        foreach (var (oldField, newField) in LegacyNames.VoiceProfileFields)
        {
            // A property name only - a quoted key followed by its colon.
            json = Regex.Replace(json, "\"" + Regex.Escape(oldField) + "\"(?=\\s*:)", "\"" + newField + "\"", RegexOptions.CultureInvariant);
        }

        return json;
    }

    /// <summary>
    /// The automatic-backup folder set on the Backups page, if one is and it
    /// is a folder we are willing to touch.
    ///
    /// This runs from Run(), at startup, before AppSettings exists - it parses
    /// settings.json itself. It is therefore the FIRST thing to touch that
    /// path on a launch after a restore, which is what made an unguarded value
    /// here leak a Windows credential to whatever machine the file named. See
    /// BackupService.SafeBackupFolder.
    /// </summary>
    private static string? ConfiguredBackupFolder()
    {
        try
        {
            var settings = Path.Combine(DataDirectory, "settings.json");
            if (!File.Exists(settings))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(settings));

            if (doc.RootElement.TryGetProperty("AutoBackupFolder", out var folder) is false
                || folder.ValueKind != JsonValueKind.String
                || folder.GetString() is not { } configured
                || string.IsNullOrWhiteSpace(configured))
            {
                return null;
            }

            // Null rather than the default when the value is refused: the
            // default folder is already renamed by the line above this
            // method's caller, so there is nothing left to do for it.
            return string.Equals(BackupService.SafeBackupFolder(configured), configured, StringComparison.Ordinal)
                ? configured
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Gives backup zips made by older versions today's name, in the folder and its safety-backup subfolder.</summary>
    private static void RenameBackups(string folder, List<string> problems)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            var folders = new[] { folder, Path.Combine(folder, "Pre-Restore Safety Backups") }.Where(Directory.Exists);

            foreach (var dir in folders)
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.zip").ToList())
                {
                    var name = Path.GetFileName(file);

                    foreach (var (oldPrefix, newPrefix) in LegacyNames.BackupPrefixes)
                    {
                        if (!name.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var target = Path.Combine(dir, newPrefix + name[oldPrefix.Length..]);

                        try
                        {
                            if (!File.Exists(target))
                            {
                                File.Move(file, target);
                            }
                        }
                        catch (Exception ex)
                        {
                            problems.Add($"{file}: {ex.Message}");
                        }

                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            problems.Add($"{folder}: {ex.Message}");
        }
    }

    private static void Log(List<string> problems)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(
                Path.Combine(DataDirectory, "crash.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  Moving data into HeatCaller's folders{Environment.NewLine}" +
                string.Join(Environment.NewLine, problems) + Environment.NewLine + new string('-', 60) + Environment.NewLine);
        }
        catch
        {
        }
    }
}
