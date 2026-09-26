namespace HeatCaller;

/// <summary>
/// What build this is.
///
/// One place, updated by hand when something ships. Deliberately not read
/// from the assembly: an assembly version moves when the project file is
/// touched, or does not move when only code changed, and neither is what
/// somebody means when they ask "which build am I running?" after a race
/// night went wrong. This number moves when a change is handed over, and
/// only then.
///
/// The convention while the app is in beta:
///
///   BETA a.b.c
///     a  stays at 0 until the app is out of beta
///     b  goes up when a whole feature lands - the track designer, race
///        replay, the pre-race check
///     c  goes up for fixes and smaller additions, and resets when b moves
///
/// Shown on the startup screen, and worth quoting in any bug report.
/// </summary>
public static class AppVersion
{
    /// <summary>The version itself. Bump this when work ships.</summary>
    public const string Number = "0.58.0";

    /// <summary>Beta until the club has run a full event on it without surprises.</summary>
    public const string Stage = "BETA";

    /// <summary>What the last bump was for - shown under the version on the startup screen.</summary>
    public const string Note = "Keyboard shortcuts - driver keys, F7/F8, M mute, Ctrl+Z, Ctrl+1-9, ? cheat sheet";

    /// <summary>"BETA 0.18.0", for a title bar or an about line.</summary>
    public static string Short => $"{Stage} {Number}";

    /// <summary>"HeatCaller - BETA 0.12.6", for anywhere that needs the name too. The rehearsal copy says so, in the title bar above all.</summary>
    public static string Full => AppPaths.IsRehearsal
        ? $"HeatCaller - {Short} - REHEARSAL (a practice copy, not the club's data)"
        : $"HeatCaller - {Short}";
}
