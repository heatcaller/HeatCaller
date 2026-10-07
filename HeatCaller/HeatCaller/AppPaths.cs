namespace HeatCaller;

/// <summary>
/// Where the app keeps its files: %AppData%\HeatCaller for everything a club
/// would back up, and %LocalAppData%\HeatCaller for what this PC can
/// rebuild on its own (the voice library, the undo holding folder).
///
/// One place, so the test project can point the whole app at a throwaway
/// folder instead of a club's real results. Every store builds its path
/// from these each time it is asked - they are properties, not fields - so
/// a redirect made before a store is touched is the only folder it ever
/// sees. StingerLibrary.Shared is the one that settles its folder once, on
/// first use; the test project redirects before any test runs, so it too
/// only ever sees the test folder.
///
/// Not covered: the default backup folder and results exports, which go to
/// Documents on purpose (BackupService.DefaultBackupFolder). A test of
/// either must pass its own folder.
///
/// AppDataMigration is the deliberate exception: it looks for the folders
/// older versions used, which are about the real machine by definition.
/// </summary>
internal static class AppPaths
{
    /// <summary>
    /// The folder name under both %AppData% and %LocalAppData%.
    ///
    /// Named rather than typed twice because something else now depends on
    /// it: Velopack installs the app into %LocalAppData%\&lt;package id&gt;, so
    /// the package id must NOT be this name, or the installer would put the
    /// app inside the voice-line cache and uninstalling would delete lines
    /// the club paid ElevenLabs for. The package id is HeatCallerApp; see
    /// publish.cmd and InstallerTests.
    /// </summary>
    internal const string FolderName = "HeatCaller";

    private static readonly string RealRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        FolderName);

    private static readonly string RealLocalRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName);

    /// <summary>The test folder everything is redirected to, or null for the real one.</summary>
    private static volatile string? _redirect;

    /// <summary>
    /// The rehearsal copy's data folder, or null when this is the real app.
    /// Set once at startup by Rehearsal.Start, from the --rehearsal switch.
    /// </summary>
    private static volatile string? _rehearsalRoot;

    /// <summary>%AppData%\HeatCaller - or, under test, the test folder's "Roaming"; or, in a rehearsal, the rehearsal copy's data folder.</summary>
    public static string Root => _redirect is { } folder
        ? Path.Combine(folder, "Roaming")
        : _rehearsalRoot ?? RealRoot;

    /// <summary>True when this run is the rehearsal copy (started with --rehearsal). See Rehearsal.</summary>
    public static bool IsRehearsal => _rehearsalRoot is not null;

    /// <summary>The club's real data folder, whatever this run is using - for Rehearsal to copy from. Never written through.</summary>
    internal static string RealDataRoot => RealRoot;

    /// <summary>
    /// Points the club's data - everything under Root - at the rehearsal
    /// copy. LocalRoot is left alone: it holds only the voice-line cache and
    /// its undo folder, a cache every line in which can be made again, and
    /// sharing it means a rehearsal does not pay ElevenLabs again for lines
    /// the club already has.
    ///
    /// Refuses a folder inside the real data folders, or one that contains
    /// them - the same guard as RedirectForTests.
    /// </summary>
    internal static void UseRehearsalRoot(string folder)
    {
        var full = Path.GetFullPath(folder);

        if (Overlaps(full, RealRoot) || Overlaps(full, RealLocalRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to use \"{full}\" for the rehearsal copy: it overlaps the real data folder.");
        }

        Directory.CreateDirectory(full);
        _rehearsalRoot = full;
    }

    /// <summary>%LocalAppData%\HeatCaller - or, under test, the test folder's "Local".</summary>
    public static string LocalRoot => _redirect is { } folder ? Path.Combine(folder, "Local") : RealLocalRoot;

    /// <summary>True once a test has pointed the app somewhere else.</summary>
    public static bool IsRedirected => _redirect is not null;

    /// <summary>
    /// Points every store at <paramref name="folder"/> instead of the real
    /// data. For the test project only.
    ///
    /// Refuses a folder inside the real data folders, or one that contains
    /// them, so a mistaken path can never aim a test that deletes its folder
    /// afterwards at a club's results.
    /// </summary>
    internal static void RedirectForTests(string folder)
    {
        var full = Path.GetFullPath(folder);

        if (Overlaps(full, RealRoot) || Overlaps(full, RealLocalRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to redirect the app's data to \"{full}\": it overlaps the real data folder.");
        }

        Directory.CreateDirectory(Path.Combine(full, "Roaming"));
        Directory.CreateDirectory(Path.Combine(full, "Local"));

        _redirect = full;
    }

    /// <summary>True when either folder is the other, or inside it.</summary>
    private static bool Overlaps(string a, string b)
    {
        var first = WithSeparator(a);
        var second = WithSeparator(b);

        return first.StartsWith(second, StringComparison.OrdinalIgnoreCase)
            || second.StartsWith(first, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithSeparator(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
}
