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
    public const string Number = "0.59.0";

    /// <summary>Beta until the club has run a full event on it without surprises.</summary>
    public const string Stage = "BETA";

    /// <summary>
    /// What the last bump was for. Not shown anywhere at the moment - the
    /// startup screen shows only the version - so it is a note for whoever
    /// bumps the version next. The release notes (website\docs\release-notes.md)
    /// have the full list, under the same number.
    /// </summary>
    public const string Note = "Everything since 0.52.0: the new name, safer race nights, rehearsal copy, timing warnings, Save Support Info, broadcast recap, CSV import";

    /// <summary>"BETA 0.18.0", for a title bar or an about line.</summary>
    public static string Short => $"{Stage} {Number}";

    /// <summary>"HeatCaller - BETA 0.12.6", for anywhere that needs the name too. The rehearsal copy says so, in the title bar above all.</summary>
    public static string Full => AppPaths.IsRehearsal
        ? $"HeatCaller - {Short} - REHEARSAL (a practice copy, not the club's data)"
        : $"HeatCaller - {Short}";
}
