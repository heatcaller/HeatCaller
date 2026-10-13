using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HeatCaller;

/// <summary>
/// The two things Windows wants before a phone on the track's Wi-Fi can open
/// HeatCaller's pages, and the one button that does them.
///
///   1. A URL RESERVATION. HttpListener binding to http://+:PORT/ - any
///      address, so phones can reach it - is administrator-only unless the
///      prefix has been reserved for ordinary users first.
///   2. A FIREWALL RULE. The half nobody mentions: without it the
///      reservation succeeds, the server starts, the page says "Running",
///      and every phone times out.
///
/// Both are admin-only, so a per-user installer cannot make them - which is
/// why this is a button in the app and not a step in Setup.exe. It asks for
/// administrator once (one UAC prompt), runs both commands, and says what
/// happened. WebServerService.UrlReservationHint stays as the fallback for a
/// locked-down PC where nobody can answer that prompt.
///
/// HeatCaller itself is never elevated by this. One short elevated cmd.exe
/// runs, does the two commands, and exits; the app stays an ordinary user
/// process, which is what it should be while it is writing the club's data.
///
/// Both commands DELETE their own entry before adding it. That is what makes
/// the button safe to press twice - which a volunteer who is not sure it
/// worked certainly will - and it avoids depending on what netsh prints when
/// something is already there, which is translated on non-English Windows and
/// so cannot be read reliably. The entries deleted are this app's own: the
/// reservation is for HeatCaller's port, and the rule is matched by
/// HeatCaller's own name.
///
/// This class has been kept to pure strings plus one method that starts a
/// process, so NetworkSetupTests can hold the commands and the reading of
/// their result to what is written here without anything being elevated or
/// changed on the machine running the tests.
/// </summary>
internal static class NetworkSetup
{
    /// <summary>How the firewall rule is named, and so also how it is found again to be replaced.</summary>
    internal static string FirewallRuleName(int port) => $"HeatCaller web {port}";

    /// <summary>
    /// The reservation. Written as an SDDL string rather than
    /// "user=Everyone": that name is translated on non-English Windows
    /// ("Jeder", "Tout le monde") and the English one then fails, with a
    /// confusing error, on exactly the laptops where nobody can read it. WD
    /// is the well-known SID for Everyone in every language. GX is
    /// GENERIC_EXECUTE, which for an HTTP prefix means "may listen on it".
    /// </summary>
    internal static string UrlAclCommand(int port) =>
        $"netsh http add urlacl url=http://+:{port}/ sddl=D:(A;;GX;;;WD)";

    /// <summary>
    /// The firewall rule. Inbound TCP on the port, private profile only: the
    /// venue's Wi-Fi set as a private network is where this belongs, and a
    /// public hotspot is not. Windows puts a newly joined Wi-Fi on Public,
    /// so the button's prompt and its result both say so - see
    /// PrivateNetworkNote.
    /// </summary>
    internal static string FirewallCommand(int port) =>
        $"netsh advfirewall firewall add rule name=\"{FirewallRuleName(port)}\" " +
        $"dir=in action=allow protocol=TCP localport={port} profile=private";

    /// <summary>
    /// The rule above only lets phones in while the Wi-Fi is set as Private,
    /// and a venue's Wi-Fi the laptop has just joined is Public until
    /// somebody changes it. Without this sentence the button says "set up"
    /// and every phone still times out.
    /// </summary>
    internal const string PrivateNetworkNote =
        "Phones can only connect while this PC's Wi-Fi is set as a Private network. Windows sets "
        + "a newly joined Wi-Fi to Public, so at a new venue check it in Settings > Network & "
        + "internet > Wi-Fi > the network's properties, and choose Private.";

    /// <summary>What a volunteer can be asked to paste into an admin Command Prompt if the button can't be used at all.</summary>
    internal static string BothCommands(int port) =>
        UrlAclCommand(port) + Environment.NewLine + FirewallCommand(port);

    // What the script's exit code says. Deliberately not 0/1: a cmd.exe that
    // failed to start the script at all, or was killed, gives 1, and that
    // must not read as "the reservation failed and the firewall worked".
    internal const int ExitBothDone = 0;
    internal const int ExitUrlAclFailed = 10;
    internal const int ExitFirewallFailed = 20;
    internal const int ExitBothFailed = 30;

    /// <summary>
    /// The batch file the elevated cmd.exe runs. A FILE, not a long
    /// /c command line: the SDDL string is full of brackets and semicolons
    /// that cmd parses differently inside a parenthesised block, and
    /// %errorlevel% in a /c string is expanded before the first command has
    /// even run. In a script both are ordinary.
    /// </summary>
    internal static string ScriptText(int port)
    {
        var script = new StringBuilder();

        script.AppendLine("@echo off");
        script.AppendLine("rem Written by HeatCaller (NetworkSetup) and run once, elevated.");
        script.AppendLine("rem Deleted again as soon as it has finished - nothing here is kept.");
        script.AppendLine("setlocal");
        // The transcript sits beside the script, and the script finds its own
        // folder (%~dp0) rather than having the path written in. The path is
        // under the user's profile, so it can hold a letter like the e in
        // "José", and cmd reads this file in the console's code page: written
        // in, it named a folder that does not exist, cmd then skipped both
        // "add" commands (it will not run a command whose redirection fails)
        // after the "delete" ones had already run. %~dp0 is filled in by cmd
        // itself, so it is right whatever the code page.
        script.AppendLine($"set \"T=%~dp0{TranscriptFileName}\"");
        script.AppendLine("set FAILED=0");
        script.AppendLine();
        script.AppendLine("echo === The URL reservation === > \"%T%\"");
        script.AppendLine($"netsh http delete urlacl url=http://+:{port}/ >nul 2>&1");
        script.AppendLine($"{UrlAclCommand(port)} >> \"%T%\" 2>&1");
        script.AppendLine($"if errorlevel 1 set /a FAILED+={ExitUrlAclFailed}");
        script.AppendLine();
        script.AppendLine("echo. >> \"%T%\"");
        script.AppendLine("echo === The firewall rule === >> \"%T%\"");
        script.AppendLine($"netsh advfirewall firewall delete rule name=\"{FirewallRuleName(port)}\" >nul 2>&1");
        script.AppendLine($"{FirewallCommand(port)} >> \"%T%\" 2>&1");
        script.AppendLine($"if errorlevel 1 set /a FAILED+={ExitFirewallFailed}");
        script.AppendLine();
        script.AppendLine("exit /b %FAILED%");

        return script.ToString();
    }

    /// <summary>The transcript's name in the script's folder. ScriptText and RunAsync must agree on it.</summary>
    internal const string TranscriptFileName = "transcript.txt";

    /// <summary>How it went, in the words the page shows.</summary>
    /// <param name="Done">Both the reservation and the rule are in place.</param>
    /// <param name="Message">What to tell the volunteer, whole sentences.</param>
    /// <param name="Transcript">What netsh itself printed, for the console and a support bundle. Empty when nothing ran.</param>
    internal sealed record Result(bool Done, string Message, string Transcript = "");

    /// <summary>
    /// What an exit code means, as a sentence. Pure, so the wording is
    /// testable without elevating anything.
    /// </summary>
    internal static Result Read(int exitCode, string transcript, int port) => exitCode switch
    {
        ExitBothDone => new Result(
            true,
            "Phone pages are set up. Windows will let other devices connect to this PC on port "
            + $"{port} from now on, and this doesn't need doing again unless the port changes. "
            + PrivateNetworkNote + " "
            + "Then start the server and open one of the addresses below on a phone.",
            transcript),

        ExitUrlAclFailed => new Result(
            false,
            "The firewall rule was added, but the URL reservation was refused - so the server still "
            + "can't accept connections from other devices. What Windows said is in the console. "
            + "Running HeatCaller as administrator is the way round it for tonight.",
            transcript),

        ExitFirewallFailed => new Result(
            false,
            "The URL reservation was made, but the firewall rule was refused - so the server will "
            + "start and phones will still time out. What Windows said is in the console. A firewall "
            + "managed by the club's IT policy is the usual reason, and they can add the rule for port "
            + $"{port} themselves.",
            transcript),

        ExitBothFailed => new Result(
            false,
            // Not "nothing was altered": each command deletes its own old
            // entry first, and those deletes have run, so a setup that used
            // to work may not any more.
            "Windows refused both changes, so phone pages are not set up. What it said is in the console. "
            + "This PC is probably locked down by policy; running HeatCaller as administrator still works.",
            transcript),

        _ => new Result(
            false,
            $"The setup didn't finish (it ended with code {exitCode}), so take it that nothing was "
            + "changed. What it managed to print is in the console.",
            transcript),
    };

    /// <summary>
    /// What netsh printed, as text. Redirected to a file, netsh writes in the
    /// console's code page (850 on German or French Windows), not UTF-8, so
    /// read as UTF-8 every translated letter in it came out as the
    /// replacement character - in the very lines support is told to read.
    /// CsvHelper.DecodeText tries strict UTF-8 first, which plain English
    /// output and a PC set to UTF-8 both pass, and only then the code page.
    /// </summary>
    internal static string ReadTranscript(byte[] bytes, Encoding oemCodePage) =>
        CsvHelper.DecodeText(bytes, oemCodePage).Trim();

    /// <summary>
    /// The console ("OEM") code page for this PC's language. Registering the
    /// code-page provider again is harmless; should the code page still not
    /// be available, Latin-1 keeps the text readable if not every letter.
    /// </summary>
    private static Encoding OemCodePage()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;

            if (codePage > 0)
            {
                return Encoding.GetEncoding(codePage);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Fall through to Latin-1.
        }

        return Encoding.Latin1;
    }

    /// <summary>When the volunteer answers No to the administrator prompt, or it is never shown to them.</summary>
    internal const int CancelledByUser = 1223;

    /// <summary>
    /// Asks for administrator once and makes both changes. Never throws: a
    /// button a club volunteer presses on a race night answers in a sentence,
    /// whatever happened.
    /// </summary>
    internal static async Task<Result> RunAsync(int port)
    {
        // Its own folder in the temp directory, so the transcript and the
        // script can be deleted together without touching anything else, and
        // two presses cannot read each other's transcript.
        var folder = Path.Combine(Path.GetTempPath(), "HeatCaller-phone-pages-" + Guid.NewGuid().ToString("N"));
        var scriptPath = Path.Combine(folder, "setup.cmd");
        var transcriptPath = Path.Combine(folder, TranscriptFileName);

        try
        {
            Directory.CreateDirectory(folder);
            // UTF-8 with NO byte-order mark. The mark is the thing that
            // matters: cmd.exe reads a batch file in the machine's own code
            // page and takes a leading BOM as three stray characters on the
            // first line, which makes it fail on "@echo off" of all things.
            // The code page itself does not come into it, because everything
            // ScriptText writes is ASCII - keep it that way (it is why the
            // transcript path is not written in; see ScriptText).
            var ascii = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            await File.WriteAllTextAsync(scriptPath, ScriptText(port), ascii).ConfigureAwait(false);

            var start = new ProcessStartInfo
            {
                FileName = "cmd.exe",

                // The script's path is quoted, and the whole /c argument is
                // quoted around that, which is cmd's rule for a path with a
                // space in it - and the temp path has one on any PC whose
                // user name does.
                Arguments = $"/c \"\"{scriptPath}\"\"",

                // Both needed together: Verb only does anything through the
                // shell, and the shell is what shows the UAC prompt.
                UseShellExecute = true,
                Verb = "runas",

                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };

            using var process = Process.Start(start);

            if (process is null)
            {
                return new Result(false, "Windows wouldn't start the setup, so nothing was changed.");
            }

            await process.WaitForExitAsync().ConfigureAwait(false);

            var transcript = File.Exists(transcriptPath)
                ? ReadTranscript(await File.ReadAllBytesAsync(transcriptPath).ConfigureAwait(false), OemCodePage())
                : string.Empty;

            return Read(process.ExitCode, transcript, port);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == CancelledByUser)
        {
            // The UAC prompt was answered No, or policy never offered it.
            // Not an error to apologise for: the volunteer said no.
            return new Result(
                false,
                "Nothing was changed - the administrator prompt was answered No. Phone pages need that "
                + "one-off permission; the two commands are in the console if somebody with an admin "
                + "account would rather run them by hand.");
        }
        catch (Exception ex)
        {
            return new Result(false, $"The setup couldn't run ({ex.GetType().Name}: {ex.Message}), so take it that nothing was changed.");
        }
        finally
        {
            // The script holds no secret, but it has served its purpose and a
            // .cmd left in the temp folder is one more thing to wonder about.
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (Exception)
            {
                // A temp folder that will not delete is nobody's problem.
            }
        }
    }
}
