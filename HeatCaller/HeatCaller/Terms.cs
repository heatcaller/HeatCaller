using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// The beta license agreement and the privacy notice, which a club must
/// accept before HeatCaller is used - and the record that it has.
///
/// WHY (28 Oct 2026): clubs were about to get the installer with no license
/// and no privacy notice anywhere in it, while driver names - some of them
/// children's - go to the relay, to ElevenLabs for the voice and to
/// Anthropic for online commentary. Both have to be accepted before
/// install, and before use.
///
/// ONE TEXT, IN TWO PLACES. Assets\Legal\HeatCaller-Terms.md is embedded in
/// the exe (read here, shown by TermsForm) and is the same file pack.cmd
/// hands vpk as --instLicense, so the .msi's own license page shows exactly
/// what the app asks for. Velopack's Setup.exe has no license page at all -
/// only the .msi does - which is why the first-launch gate exists as well:
/// whichever way HeatCaller was installed (the .msi, Setup.exe, a silent
/// msiexec, a source build), it does nothing until the current text has
/// been accepted.
///
/// WHAT COUNTS AS THE VERSION. The SHA-256 of the text itself, not a number
/// someone has to remember to bump: any change to the file, a comma
/// included, asks again. <see cref="Edition"/> is the human-readable date
/// that goes with it, recorded beside the hash so the record says which
/// terms were accepted in words as well.
/// </summary>
internal static class Terms
{
    /// <summary>The name the text is embedded under - see the project file.</summary>
    internal const string ResourceName = "HeatCaller.Terms.md";

    /// <summary>The file the acceptance is kept in, under AppPaths.Root.</summary>
    internal const string AcceptanceFileName = "terms-accepted.json";

    /// <summary>The terms' own date, as their first line after the title gives it. Kept in the record beside the hash.</summary>
    internal const string Edition = "Beta, 25 November 2026 (revision 4)";

    private static string? _text;

    /// <summary>The terms as written (Markdown). Empty when the resource is missing from the build.</summary>
    internal static string Text => _text ??= Load();

    /// <summary>The SHA-256 of <see cref="Text"/>, lower-case hex - what an acceptance is held to.</summary>
    internal static string Hash => HashOf(Text);

    /// <summary>One acceptance, as the file keeps it.</summary>
    /// <param name="Hash">The SHA-256 of the text that was accepted.</param>
    /// <param name="Edition">That text's date, in words.</param>
    /// <param name="AcceptedUtc">When.</param>
    /// <param name="AppVersion">The HeatCaller version that asked.</param>
    /// <param name="WindowsUser">The Windows account that accepted.</param>
    internal sealed record Acceptance(
        string Hash,
        string Edition,
        DateTime AcceptedUtc,
        string AppVersion,
        string WindowsUser);

    internal static string HashOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(text)))).ToLowerInvariant();

    /// <summary>
    /// Line endings made the same before hashing, so git turning LF into CRLF
    /// on a checkout - or not - is not taken for new terms.
    /// </summary>
    private static string Normalise(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string Load()
    {
        using var stream = typeof(Terms).Assembly.GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            return "";
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Whether the current text has been accepted on this PC. In a
    /// rehearsal, an acceptance in the club's real data folder counts too -
    /// it is the same PC and the same club - though one is only ever written
    /// into the rehearsal's own copy (AppPaths.RealDataRoot is never written
    /// through).
    /// </summary>
    internal static bool IsAccepted()
    {
        var hash = Hash;

        if (Read(Path.Combine(AppPaths.Root, AcceptanceFileName)) is { } here && here.Hash == hash)
        {
            return true;
        }

        return AppPaths.IsRehearsal
            && Read(Path.Combine(AppPaths.RealDataRoot, AcceptanceFileName)) is { } real
            && real.Hash == hash;
    }

    /// <summary>The acceptance kept in <paramref name="path"/>, or null when there is none or it cannot be read.</summary>
    internal static Acceptance? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Acceptance>(SafeFile.ReadAllTextWithRetry(path));
        }
        catch
        {
            // A damaged record asks again; it never lets anyone past.
            return null;
        }
    }

    /// <summary>
    /// Records that the current text was accepted, now. Returns false when
    /// the file could not be written - the club is let in all the same this
    /// time (it did accept), and is asked again next time.
    /// </summary>
    internal static bool RecordAcceptance()
    {
        var record = new Acceptance(
            Hash,
            Edition,
            DateTime.UtcNow,
            AppVersion.Number,
            Environment.UserName);

        return SafeFile.WriteAllText(
            Path.Combine(AppPaths.Root, AcceptanceFileName),
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// The Markdown made plain for a text box: headings in capitals with a
    /// blank line before them, bold and quote marks taken off, bullets as
    /// bullets. Nothing is dropped - every word of the terms is shown.
    /// </summary>
    internal static string ForReading(string markdown)
    {
        var lines = Normalise(markdown).Split('\n');
        var output = new StringBuilder();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                if (output.Length > 0 && !output.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    output.Append("\r\n");
                }

                var title = Plain(heading.Groups[2].Value);
                output.Append(heading.Groups[1].Value.Length <= 1 ? title.ToUpperInvariant() : title).Append("\r\n");
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                output.Append("  • ").Append(Plain(line[2..])).Append("\r\n");
                continue;
            }

            output.Append(Plain(line.TrimStart('>', ' '))).Append("\r\n");
        }

        return output.ToString().TrimEnd() + "\r\n";
    }

    /// <summary>A line with Markdown's emphasis taken off.</summary>
    private static string Plain(string line) => line.Replace("**", "").Replace("__", "");
}
