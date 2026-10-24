using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// Hands the timing library (HeatCaller.Timing) the two app pieces it uses
/// but cannot reference: the session log and the app's version.
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer P6): a module initializer rather than a
/// line in Program.Main, so it has run before ANY code in the app runs -
/// Main's first line, a static constructor, or a test in HeatCaller.Tests
/// (whose own module initializers touch the app first) - and nothing can
/// reach the race journal or a timing recording before the hooks are in
/// place. It only stores two delegates: no file, no window, no thread, so
/// it is safe even during Velopack's install hooks, which run at the top of
/// Main. See TimingHooks in the library for why these two could not move.
/// </remarks>
internal static class TimingLibraryHooks
{
    [ModuleInitializer]
    internal static void Install()
    {
        TimingHooks.Log = SessionLog.Write;
        TimingHooks.AppVersionShort = () => AppVersion.Short;
    }
}
