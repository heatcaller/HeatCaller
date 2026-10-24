namespace HeatCaller;

/// <summary>
/// The two things the timing library needs from the app and cannot reach
/// itself, handed in by the app before anything else runs.
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer P6): the library must not reference the
/// app (that is the whole point of it), but two of the files that moved in
/// used app types that cannot move with them:
///
/// - SessionLog (RaceJournal's two startup lines). It reads
///   Rehearsal.Folder and AppVersion, both app-only.
/// - AppVersion.Short (the header of a timing recording). AppVersion reads
///   the version from the assembly it lives in, and the version number is
///   set on the APP (HeatCaller.csproj); moved here it would read this
///   library's own version instead.
///
/// So the app sets both from a module initializer (TimingLibraryHooks in
/// the app), which runs before Main and before any test touches the app.
/// The defaults are what each call already did when there was nowhere to
/// write: SessionLog.Write is silent until SessionLog.Start, and the
/// recorder's header already fell back to an empty version on any error.
/// Set once, before any other thread exists, and only read after that.
/// </remarks>
internal static class TimingHooks
{
    /// <summary>One line for the session log. The app points it at SessionLog.Write.</summary>
    internal static Action<string> Log { get; set; } = _ => { };

    /// <summary>The app's version as the title bar shows it ("BETA 0.60.0"). The app points it at AppVersion.Short.</summary>
    internal static Func<string> AppVersionShort { get; set; } = () => string.Empty;
}
