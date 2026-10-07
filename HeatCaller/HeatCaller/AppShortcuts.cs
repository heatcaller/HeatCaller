using System.Diagnostics;

namespace HeatCaller;

/// <summary>
/// The one shortcut Velopack does not make: HeatCaller Rehearsal.
///
/// Velopack's installer makes the ordinary Start menu and desktop shortcuts
/// itself (vpk pack --shortcuts). The rehearsal copy is ours, because it is
/// the same exe with --rehearsal on it, and nothing in a package format knows
/// that a second shortcut to the same program with a switch is wanted. See
/// Rehearsal: it runs HeatCaller against a copy of the club's data, for
/// practising a race night without touching the real results, and a volunteer
/// is never going to find it by typing a command-line switch.
///
/// Called from Program's Velopack hooks: made after an install, taken away
/// before an uninstall. Those hooks are FAST CALLBACKS - Velopack runs them
/// and then exits the process, killing them after 30 seconds - so everything
/// here is quick, and nothing here is allowed to throw: a shortcut that could
/// not be made must not be the reason an install appears to fail.
///
/// IT POINTS AT THE STUB, not at the exe inside current\. Velopack installs
/// to %LocalAppData%\HeatCallerApp\current\ and swaps that folder on every
/// update, with a small stub exe in the folder above it that always starts
/// whatever the current version is and passes its arguments on. A shortcut to
/// current\HeatCaller.exe would work until the first update moved it.
/// </summary>
internal static class AppShortcuts
{
    /// <summary>What the shortcut is called, and so where it is looked for again to be removed.</summary>
    internal const string RehearsalName = "HeatCaller Rehearsal";

    /// <summary>Its one-line description, which Windows shows as a tooltip.</summary>
    internal const string RehearsalDescription =
        "Practise a race night on a copy of the club's data - nothing you do here touches the real results.";

    /// <summary>Where it goes: this user's Start menu, beside the shortcut Velopack made.</summary>
    internal static string RehearsalShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        RehearsalName + ".lnk");

    /// <summary>
    /// The exe the shortcut should start: Velopack's stub in the folder above
    /// this one, which survives every update, falling back to this very exe
    /// if the layout is not what we expect (a portable build, a developer
    /// running from bin\Debug) so that a shortcut made by hand still works.
    /// </summary>
    internal static string LauncherPath()
    {
        try
        {
            var current = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));

            if (current is not null)
            {
                var stub = Path.Combine(current, "HeatCaller.exe");

                if (File.Exists(stub))
                {
                    return stub;
                }
            }
        }
        catch (Exception)
        {
            // Fall through to this process's own path.
        }

        return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HeatCaller.exe");
    }

    /// <summary>
    /// Makes the rehearsal shortcut. Quiet about everything: there is no
    /// window open to tell and no user watching - this runs inside the
    /// installer.
    /// </summary>
    internal static void CreateRehearsal()
    {
        var exe = LauncherPath();
        var lnk = RehearsalShortcutPath;

        // A .lnk has no text format to write - it is a shell object - and
        // .NET has no API for one. WScript.Shell is the way every installer
        // on Windows does it, reached through PowerShell rather than a COM
        // reference so this app keeps no COM interop of its own for one
        // shortcut made once.
        var script =
            $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut({PowerShellString(lnk)}); " +
            $"$s.TargetPath = {PowerShellString(exe)}; " +
            $"$s.Arguments = '--rehearsal'; " +
            $"$s.WorkingDirectory = {PowerShellString(Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory)}; " +
            $"$s.Description = {PowerShellString(RehearsalDescription)}; " +
            "$s.Save()";

        RunPowerShell(script);
    }

    /// <summary>
    /// Takes it away again. Before an uninstall, so the Start menu is not
    /// left with a shortcut to an exe that has gone - Velopack removes its
    /// own shortcuts and knows nothing about this one.
    /// </summary>
    internal static void RemoveRehearsal()
    {
        try
        {
            File.Delete(RehearsalShortcutPath);
        }
        catch (Exception)
        {
            // A shortcut that will not delete is not worth failing an
            // uninstall over - and File.Delete on a path that is not there
            // does nothing at all, which is the common case on a machine
            // where the shortcut was never made.
        }
    }

    /// <summary>
    /// A string PowerShell will read as the literal text it is given: single
    /// quoted, with any single quote in it doubled. A path cannot contain a
    /// quote on Windows, but this is a string going into a command line and
    /// it is quoted properly rather than almost properly.
    /// </summary>
    internal static string PowerShellString(string value) => "'" + value.Replace("'", "''") + "'";

    private static void RunPowerShell(string script)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",

                // -NonInteractive and -NoProfile: nothing may stop to ask,
                // and a user's own PowerShell profile must not run inside an
                // installer. -ExecutionPolicy Bypass is for -Command on a
                // machine whose policy is Restricted; nothing is read from
                // disk, so there is no script file to trust.
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"",

                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(start);

            // Well inside the hook's 30 seconds, and it has to be waited for:
            // Velopack exits the process the moment the hook returns, which
            // would kill PowerShell part way through writing the shortcut.
            process?.WaitForExit(milliseconds: 20_000);
        }
        catch (Exception)
        {
            // See the class comment: a shortcut is not worth failing an
            // install over.
        }
    }
}
