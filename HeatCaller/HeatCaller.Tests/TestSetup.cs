using System.Runtime.CompilerServices;

// One test at a time. AppPaths is one folder for the whole process, and each
// test that touches files points it at a folder of its own (TempAppData) -
// two running at once would write into each other's.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HeatCaller.Tests;

/// <summary>
/// Points the app at a throwaway folder the moment this assembly loads.
///
/// A module initializer runs before any test in the assembly, so before any
/// app type has built a path - a test that forgets TempAppData still lands
/// in %TEMP%, never in the club's real %AppData%\HeatCaller.
/// </summary>
internal static class TestSetup
{
    /// <summary>This run's folder, under %TEMP%\HeatCaller.Tests. Each test's own folder goes inside it.</summary>
    public static string SessionRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "HeatCaller.Tests",
        $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}");

    [ModuleInitializer]
    internal static void RedirectAppData() => AppPaths.RedirectForTests(SessionRoot);

    /// <summary>
    /// No sound out of the test run.
    ///
    /// Several tests drive the announcer far enough that it really plays:
    /// the warning buzzer and its spoken line, in the Kokoro voice on a
    /// default install, two calls over each other. A test run is not a
    /// race night and must not sound like one - and a test whose timing
    /// depends on an audio device is a test that fails on a machine with
    /// no speaker.
    /// </summary>
    [ModuleInitializer]
    internal static void SilenceAudio() => AudioBus.Silent = true;

    /// <summary>
    /// No race recordings out of the test run either.
    ///
    /// Race audio goes to Documents on purpose - so that a backup can never
    /// zip a night of mp3s - and RedirectAppData above does not reach
    /// Documents (AppPaths says so itself). Without this, every test that
    /// drives a race to the green writes a real wav into the real
    /// Documents\HeatCaller Race Audio folder of whoever ran the suite, and
    /// RaceNightTests plays a whole race night through. RaceAudioTests is the
    /// one class that needs it on, because it is the class that tests it: it
    /// turns the flag off in its constructor and back on in Dispose, having
    /// pointed the store at its own temp folder first.
    /// </summary>
    [ModuleInitializer]
    internal static void NoRaceRecordings() => RaceAudioStore.OffForTests = true;

    /// <summary>
    /// Windows set up the way the app sets them up (ApplicationConfiguration
    /// .Initialize, from HeatCaller.csproj) - before any test makes one,
    /// since none of these can be changed once a window exists.
    ///
    /// It matters to anything that measures: the layout test (LayoutTests)
    /// builds every page and measures what comes out, and text is measured
    /// differently without these - by GDI+ rather than GDI, at the screen's
    /// real DPI rather than the 96 the app always sees, with the old
    /// unthemed check boxes. A report measured that way would describe a
    /// different app.
    /// </summary>
    [ModuleInitializer]
    internal static void SetUpWindowsLikeTheApp()
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
        catch (InvalidOperationException)
        {
            // A window already exists, so it is too late - measured as it
            // stands. Not a reason to stop every other test running.
        }
    }
}

/// <summary>
/// An empty data folder for one test. Create it as a field of the test class
/// (xUnit makes a new instance per test) and dispose it from the class's
/// Dispose; the folder goes when the test ends.
/// </summary>
public sealed class TempAppData : IDisposable
{
    public TempAppData()
    {
        Folder = Path.Combine(TestSetup.SessionRoot, "t-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.RedirectForTests(Folder);
    }

    /// <summary>The test's folder; the app sees its "Roaming" as %AppData%\HeatCaller.</summary>
    public string Folder { get; }

    /// <summary>A file where the app would keep it: Path.Combine(AppPaths.Root, name).</summary>
    public string PathOf(string name) => Path.Combine(AppPaths.Root, name);

    public void Dispose()
    {
        AppPaths.RedirectForTests(TestSetup.SessionRoot);

        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch
        {
            // A file still held open by the test - left in %TEMP% for Windows to clear.
        }
    }
}
