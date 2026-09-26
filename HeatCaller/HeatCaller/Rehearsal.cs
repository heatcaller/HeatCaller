using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller;

/// <summary>
/// The rehearsal copy: HeatCaller started with --rehearsal, running on a
/// copy of the club's data in a folder of its own, for practising a race
/// night - real decoder, real cars, the phone pages - without touching the
/// club's real results, drivers or backups.
///
/// WHY. HeatCaller's own rule is that nothing new ships until race nights
/// have run clean, and a club of one has no race nights to run. A rehearsal
/// is the next best thing, and it has to be safe to do the things a
/// rehearsal is for: build a programme, run heats, correct a result, crash
/// the app mid-race, throw the whole night away.
///
/// HOW.
///  - The first rehearsal copies the club's data folder
///    (%AppData%\HeatCaller) into %LocalAppData%\HeatCaller Rehearsal\Data,
///    and every later one carries on from there. To start again from the
///    club's data as it is now, delete that folder.
///  - AppPaths.Root points at the copy for the whole run, so every store,
///    the settings and the crash journal read and write there. LocalRoot is
///    shared: it only holds the voice-line cache, which can all be made again.
///  - Backups and results cards go to the rehearsal's own folder
///    (BackupService.SafeBackupFolder, DocumentsFolder below), never to the
///    club's Documents folders, where backup pruning would delete the club's
///    real backups to make room for the rehearsal's.
///  - Nothing is emailed (PinMailer): the drivers are the club's real ones.
///  - Publishing to the club website is switched OFF in the copy when it is
///    made. The copy has the club's own relay settings, so switched back on
///    (Club Website page) it publishes to the club's real website - which is
///    what a rehearsal of online check-in or the race-day manager needs, and
///    is said in the startup note so it is never a surprise.
///  - The title bar and the console say REHEARSAL.
///
/// Not copied: the undo holding folder, the crash log, and a race journal -
/// a race the real app was running when it last closed is the club's to
/// recover, not the rehearsal's.
/// </summary>
internal static class Rehearsal
{
    /// <summary>The command-line switch.</summary>
    public const string Switch = "--rehearsal";

    /// <summary>What the copy leaves behind: the undo folder, the crash logs and a race in progress.</summary>
    private static readonly HashSet<string> NotCopied = new(StringComparer.OrdinalIgnoreCase)
    {
        UndoHoldingFolder.FolderName,
        "race-in-progress.json",
        "crash.log",
        "crash.old.log",
    };

    /// <summary>What the startup note says, once Start has run; null for the real app.</summary>
    private static string? _note;

    /// <summary>%LocalAppData%\HeatCaller Rehearsal - the rehearsal's own folder: its data, backups and results cards.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HeatCaller Rehearsal");

    /// <summary>The copy of the club's data the rehearsal runs on.</summary>
    public static string DataFolder => Path.Combine(Folder, "Data");

    /// <summary>True when the command line asks for the rehearsal copy. The first argument is the program itself.</summary>
    public static bool Wanted(IReadOnlyList<string> commandLine) =>
        commandLine.Skip(1).Any(a => string.Equals(a.Trim(), Switch, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A folder the app would otherwise make under Documents - "HeatCaller
    /// Backups", "HeatCaller Results" - or, in a rehearsal, the same name
    /// under the rehearsal's own folder.
    /// </summary>
    public static string DocumentsFolder(string name) => AppPaths.IsRehearsal
        ? Path.Combine(Folder, name)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name);

    /// <summary>
    /// Makes this run the rehearsal copy: copies the club's data the first
    /// time, then points the app at the copy. False, with the reason, when
    /// it could not be done - and then the app must not start at all rather
    /// than run on the real data.
    /// </summary>
    public static bool Start(out string problem)
    {
        problem = string.Empty;

        try
        {
            var made = Prepare(AppPaths.RealDataRoot, DataFolder);

            AppPaths.UseRehearsalRoot(DataFolder);

            _note = $"{AppTime.LogStampNow()}  [rehearsal] This is the REHEARSAL copy of HeatCaller, running on {DataFolder}"
                    + (made
                        ? " - copied from the club's data just now. Publishing to the club website has been switched off in the copy"
                          + " (turn it on from the Club Website page to rehearse online check-in or the phone manager - it then"
                          + " publishes to the club's real website)."
                        : " - the copy made by an earlier rehearsal. Delete that folder to start again from the club's data.")
                    + " Nothing is emailed, and backups and results cards stay in the rehearsal's own folder."
                    + " The club's real data is not touched.";

            return true;
        }
        catch (Exception ex)
        {
            problem = $"{ex.Message}\r\n\r\nThe rehearsal folder is {Folder}.";
            return false;
        }
    }

    /// <summary>The startup note for the console, or null for the real app.</summary>
    public static string? StartupNote() => _note;

    /// <summary>
    /// Copies <paramref name="realData"/> into <paramref name="copy"/> when
    /// the copy does not exist yet or is empty, and switches publishing off
    /// in it. True when a copy was made; false when an earlier one was kept.
    /// Separate from Start so it can be tested on folders of its own.
    /// </summary>
    internal static bool Prepare(string realData, string copy)
    {
        if (Directory.Exists(copy) && Directory.EnumerateFileSystemEntries(copy).Any())
        {
            return false;
        }

        Directory.CreateDirectory(copy);

        if (Directory.Exists(realData))
        {
            CopyFolder(realData, copy);
        }

        TurnPublishingOff(Path.Combine(copy, "settings.json"));

        return true;
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from))
        {
            if (!NotCopied.Contains(Path.GetFileName(file)))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
            }
        }

        foreach (var folder in Directory.EnumerateDirectories(from))
        {
            if (!NotCopied.Contains(Path.GetFileName(folder)))
            {
                CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
            }
        }
    }

    /// <summary>
    /// Publishing to the club website and emailing PINs off, in the copy's
    /// settings.json. Edited as JSON rather than through AppSettings, which
    /// would read the copy's sealed secrets and save the whole file again;
    /// every other setting is left exactly as written. No settings file (a
    /// club with no data yet) means nothing to switch off.
    /// </summary>
    internal static void TurnPublishingOff(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return;
        }

        if (JsonNode.Parse(File.ReadAllText(settingsPath)) is not JsonObject settings)
        {
            return;
        }

        settings["RelayEnabled"] = false;
        settings["RacerPinsEmailAutomatically"] = false;

        File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
