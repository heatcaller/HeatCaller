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
