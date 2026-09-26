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
    private static readonly string RealRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeatCaller");

    private static readonly string RealLocalRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HeatCaller");

    /// <summary>The test folder everything is redirected to, or null for the real one.</summary>
    private static volatile string? _redirect;

    /// <summary>%AppData%\HeatCaller - or, under test, the test folder's "Roaming".</summary>
    public static string Root => _redirect is { } folder ? Path.Combine(folder, "Roaming") : RealRoot;

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
