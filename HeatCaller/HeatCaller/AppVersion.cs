using System.Reflection;

namespace HeatCaller;

/// <summary>
/// What build this is.
///
/// One place: the csproj's &lt;Version&gt;. It used to be typed in here by
/// hand, on the argument that an assembly version can move without anything
/// actually shipping - but the installer (Velopack) numbers the package and
/// the update feed from the assembly, so the package, the updater and the
/// title bar could each claim a different build. The csproj wins, and
/// Number reads it back at runtime, so there is nothing left to disagree
/// with. Bump it in HeatCaller.csproj when work is handed over, and only
/// then.
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
    /// <summary>
    /// The version itself, read out of the assembly the app is running from.
    /// Bump it in HeatCaller.csproj (&lt;Version&gt;) when work ships. Read
    /// once and kept: nothing about it changes while the app runs, and it is
    /// wanted from static code with no window open (the splash screen, the
    /// session log's first line).
    /// </summary>
    public static string Number { get; } = ReadNumber();

    /// <summary>
    /// What to show if reflection somehow gives nothing - a stripped single
    /// file, an odd host. A stale number is still better than a blank one in
    /// a bug report, and it must never be the reason the app fails to start.
    /// </summary>
    private const string Unknown = "0.0.0";

    private static string ReadNumber()
    {
        try
        {
            var assembly = typeof(AppVersion).Assembly;

            // &lt;Version&gt; sets the informational version, which is the
            // one that keeps "0.59.0" as written - the plain assembly
            // version is padded to four parts (0.59.0.0).
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            var number = WithoutBuildMetadata(informational);
            if (number.Length > 0)
            {
                return number;
            }

            var version = assembly.GetName().Version;
            if (version is not null)
            {
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
        }
        catch (Exception)
        {
            // Nothing here is worth a crash on startup; fall through.
        }

        return Unknown;
    }

    /// <summary>
    /// "0.59.0+9f3c1ab" -> "0.59.0". The SDK appends "+&lt;git sha&gt;" to the
    /// informational version on some builds, which is no use to anybody
    /// reading a title bar.
    /// </summary>
    internal static string WithoutBuildMetadata(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var trimmed = version.Trim();
        var plus = trimmed.IndexOf('+');
        return plus < 0 ? trimmed : trimmed[..plus].Trim();
    }

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
