using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// Takes a club's private details out of text before it leaves the laptop -
/// the support file (SupportBundle) is its one user so far.
/// </summary>
/// <remarks>
/// In this order, because each step would spoil the next:
///
///  1. <b>Folder paths under Users</b>: <c>C:\Users\asher\Documents</c>
///     becomes <c>C:\Users\[user]\Documents</c>. HeatCaller logs file paths,
///     and every one of them carries the Windows user name. This PC's own
///     user folder is replaced exactly, whatever is in its name; any other
///     folder under Users up to the next space or separator. First, so a
///     user name that is also a driver's first name is taken as the folder
///     it is.
///  2. <b>Email addresses</b> become <c>[email]</c> - before names, so an
///     address made of a name goes whole rather than as
///     <c>[driver 1].smith@...</c>.
///  3. <b>Driver names and contact details</b> from Driver Hub. Each
///     driver gets a number, and every form of their name - the full name,
///     the announcer's name, and each word of either - becomes
///     <c>[driver n]</c>, so one driver can still be followed through a
///     night's log. Matched as whole words - as typed, in capitals, and with
///     a capital and the rest small ("Asher", "ASHER") - but not in lower
///     case unless that is how it was typed: a driver called Max or Will
///     would otherwise take every "max" and "will" in the log with them.
///
/// Best effort, and said so wherever it is used: a name that isn't in
/// Driver Hub can't be recognised.
/// </remarks>
internal sealed class Redactor
{
    /// <summary>One person to hide: every name they go by, and their contact details.</summary>
    internal sealed record Person(IReadOnlyList<string> Names, IReadOnlyList<string> Contacts);

    /// <summary>
    /// Any folder under Users, up to the next separator or space - a user
    /// name can hold a space, but a log line carries on after a path, and
    /// taking everything to the end of it would lose the line. This PC's
    /// own folder, space or not, is replaced exactly before this runs.
    /// Backslashes only: a Windows path. A web address with /users/ in it
    /// is left alone.
    /// </summary>
    private static readonly Regex UsersFolder = new(
        @"\\Users\\[^\\/:*?""<>|\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Email = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>What each name or contact is replaced with, keyed exactly as it is matched.</summary>
    private readonly Dictionary<string, string> _replacements;

    /// <summary>Every name and contact at once; null when there are none.</summary>
    private readonly Regex? _people;

    /// <summary>This PC's user folder (C:\Users\asher), as a whole folder name - not the start of C:\Users\ashley. Null when not known.</summary>
    private readonly Regex? _userFolder;

    /// <summary>What <see cref="_userFolder"/> becomes: C:\Users\[user].</summary>
    private readonly string _userFolderReplacement = string.Empty;

    private Redactor(Dictionary<string, string> replacements, string? userProfileFolder)
    {
        _replacements = replacements;

        var folder = userProfileFolder?.TrimEnd('\\', '/');
        var parent = string.IsNullOrEmpty(folder) ? null : Path.GetDirectoryName(folder);

        if (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(parent))
        {
            // Either separator between the parts: C:\Users\asher and
            // C:/Users/asher are the same folder.
            var parts = folder.Split('\\', '/').Select(Regex.Escape);

            _userFolder = new Regex(
                string.Join(@"[\\/]", parts) + @"(?![\p{L}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            _userFolderReplacement = Path.Combine(parent, "[user]").Replace("$", "$$");
        }

        if (replacements.Count == 0)
        {
            return;
        }

        // Longest first, so "Asher Kent" is taken whole before "Asher" can
        // take the start of it. Whole words only: not inside another word,
        // and not inside a transponder number.
        var alternatives = replacements.Keys
            .OrderByDescending(k => k.Length)
            .ThenBy(k => k, StringComparer.Ordinal)
            .Select(Regex.Escape);

        _people = new Regex(
            $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join("|", alternatives)})(?![\p{{L}}\p{{N}}])",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    /// <summary>A redactor that only takes out folder paths and emails.</summary>
    internal static Redactor None { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal), userProfileFolder: null);

    /// <summary>
    /// Builds one for these people, numbered in the order given - driver 1
    /// is the first. A name two drivers share keeps the first one's number.
    /// </summary>
    /// <param name="userProfileFolder">This PC's user folder (Environment.SpecialFolder.UserProfile), replaced exactly wherever it appears.</param>
    internal static Redactor For(IEnumerable<Person> people, string? userProfileFolder)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        var number = 0;

        foreach (var person in people)
        {
            var terms = person.Names.SelectMany(NameForms).ToList();

            if (terms.Count > 0)
            {
                number++;
                var label = $"[driver {number}]";

                foreach (var term in terms)
                {
                    replacements.TryAdd(term, label);
                }
            }

            foreach (var contact in person.Contacts)
            {
                var trimmed = contact.Trim();

                // Four characters at least: a contact of "1" or "no" would
                // take half the log with it.
                if (trimmed.Length >= 4)
                {
                    replacements.TryAdd(trimmed, "[contact]");
                }
            }
        }

        return new Redactor(replacements, userProfileFolder);
    }

    /// <summary>
    /// Every form of one name that is looked for: the name itself and each
    /// word of it, each as written and in capitals. A word needs two letters
    /// to count - an initial on its own would take every "A" in the log.
    /// </summary>
    private static IEnumerable<string> NameForms(string name)
    {
        var whole = string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var forms = new List<string>();

        if (whole.Count(char.IsLetter) >= 2)
        {
            forms.Add(whole);
        }

        foreach (var word in whole.Split(' '))
        {
            // Quotes, brackets and full stops round a nickname or an initial
            // are not part of the name.
            var trimmed = word.Trim('"', '\'', '(', ')', '[', ']', ',', '.', '\u201C', '\u201D', '\u2018', '\u2019');

            if (trimmed.Count(char.IsLetter) >= 2)
            {
                forms.Add(trimmed);
            }
        }

        // In capitals, and each word with a capital and the rest small - so
        // a profile typed "ASHER KENT" or "asher kent" still finds the
        // "Asher" the announcer's lines are written with.
        foreach (var form in forms.ToList())
        {
            forms.Add(form.ToUpperInvariant());
            forms.Add(string.Join(" ", form.Split(' ').Select(TitleCase)));
        }

        return forms.Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// "asher" and "ASHER" to "Asher" - with a capital after a hyphen or an
    /// apostrophe too, so "JEAN-LUC O'NEIL" becomes "Jean-Luc O'Neil". A
    /// capital anywhere else ("McLaren") can't be guessed; the form as typed
    /// is looked for as well.
    /// </summary>
    private static string TitleCase(string word)
    {
        var chars = word.ToLowerInvariant().ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 0 || chars[i - 1] is '-' or '\'' or '\u2019')
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
            }
        }

        return new string(chars);
    }

    /// <summary>Returns the line with everything private replaced; <paramref name="changed"/> says whether anything was.</summary>
    internal string Apply(string line, out bool changed)
    {
        var result = line;

        if (_userFolder is not null)
        {
            result = _userFolder.Replace(result, _userFolderReplacement);
        }

        result = UsersFolder.Replace(result, @"\Users\[user]");
        result = Email.Replace(result, "[email]");

        if (_people is not null)
        {
            result = _people.Replace(result, m => _replacements.TryGetValue(m.Value, out var label) ? label : m.Value);
        }

        changed = !string.Equals(result, line, StringComparison.Ordinal);
        return result;
    }
}
