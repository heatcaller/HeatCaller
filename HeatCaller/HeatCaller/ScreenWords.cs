using System.Text;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// Two word lists for commentary (15 Nov 2026, with the Sailor-mouth tone).
///
/// HATE TERMS - slurs about race, religion, ethnicity, nationality,
/// sexuality, gender identity or disability. Checked on every line the model
/// writes, in every tone, before it is used; a line with one is dropped,
/// never repaired. Asher: "never racist or antisemitic". The prompt says so
/// too, but a prompt is a request and this is the promise.
///
/// SWEARS - starred on anything SHOWN (the Big Screen, the phones, the
/// overlay, the video captions) while the voice says them as written.
/// Swearing is for the room; a screenshot travels.
///
/// These are deliberately two lists: a swear is allowed and masked, a hate
/// term is never allowed at all.
/// </summary>
internal static class ScreenWords
{
    /// <summary>
    /// Slurs matched as the WHOLE word, or with a plain plural or "-ed"
    /// ending ("kikes", "retarded") - short ones that would otherwise catch
    /// ordinary words ("fag" in nothing, "paki" not "pakistan", "retard" not
    /// "fire retardant").
    ///
    /// Left out on purpose: words with an everyday meaning a race call uses
    /// ("a chink in his armor", "poof, he's gone"). The prompt's hard lines
    /// cover those; this list is for words that are slurs in every use.
    /// </summary>
    private static readonly HashSet<string> HateWords = new(StringComparer.Ordinal)
    {
        // Race and ethnicity
        "gook", "beaner", "paki", "spic", "spick", "kaffir", "coolie", "gyppo", "pikey", "wop", "dago", "slanteye",
        // Antisemitic
        "kike", "yid", "heeb", "hymie", "sheeny", "jewed",
        // Sexuality and gender identity
        "fag", "dyke", "tranny", "shemale",
        // Disability
        "retard", "spaz", "mong",
    };

    /// <summary>
    /// Slurs long and unambiguous enough to match at the START of a word,
    /// so every ending and compound is caught too.
    /// </summary>
    private static readonly string[] HateStems =
    {
        "nigger", "nigga", "sandnigger", "wetback", "raghead", "towelhead", "jigaboo", "porchmonkey", "golliwog",
        "zipperhead", "christkiller", "jewboy", "faggot", "poofter",
    };

    /// <summary>Phrases that are hateful though each word alone is not. Matched as whole words.</summary>
    private static readonly string[] HatePhrases =
    {
        "jew him down", "jew them down", "gas the jews", "porch monkey", "white power", "heil hitler", "sieg heil",
        "1488", "dirty jew", "filthy jew",
    };

    /// <summary>
    /// Swears to star on screens. Whole words with only their own endings
    /// listed, so an ordinary word is never starred by accident -
    /// "Pissarro", the Wankel engine, "Shitara", "hello", "Dickson" all show
    /// as written. "Fuck" alone takes any ending: no name starts with it.
    ///
    /// The milder ones too (15 Nov 2026) - dick, cock, prick, crap, damn,
    /// hell. A driver CALLED Dick is the reason for the keep list on
    /// <see cref="Masked"/>: his name shows as it is.
    /// </summary>
    private static readonly Regex Swears = new(
        @"\b(?:mother)?f+u+c+k\w*"
        + @"|\b(?:bull)?shit(?:s|ty|tier|ting|ted|head|heads|show)?\b"
        + @"|\bbastards?\b|\bbollocks?\b|\bpiss(?:ed|es|ing|y)?\b"
        + @"|\b(?:ass|arse)(?:holes?)?\b|\bwank(?:er|ers|ing|ed)?\b|\btwats?\b"
        + @"|\bbitch(?:es|ing|y)?\b|\bcunts?\b|\b(?:god)?damn(?:ed|it)?\b"
        + @"|\bdick(?:s|head|heads)?\b|\bcocks?\b|\bpricks?\b|\bcrap(?:s|py|pier)?\b|\bhell\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Word = new(@"[a-z0-9]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The first hate term in <paramref name="written"/>, or null when there
    /// is none. Read through simple disguises - "k1ke", "f.a.g", "n*gger" -
    /// by folding look-alike digits and dropping what sits between letters.
    ///
    /// A term that is also in <paramref name="allowedFrom"/> (the facts and
    /// prompt the model was given) is not counted: a club whose driver is
    /// called "Mong" typed that name, and a call that says it is not the
    /// model being hateful.
    /// </summary>
    public static string? HateTermIn(string? written, string? allowedFrom = null)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        var text = Fold(written);
        var allowed = string.IsNullOrEmpty(allowedFrom) ? string.Empty : Fold(allowedFrom);
        var words = Word.Matches(text).Select(m => m.Value).ToList();
        var allowedWords = Word.Matches(allowed).Select(m => m.Value).ToHashSet();

        // Letters run together, for "f.a.g" and "k i k e".
        var squashed = SquashedSingles(words);

        foreach (var word in words.Concat(squashed))
        {
            if (allowedWords.Contains(word))
            {
                continue;
            }

            if (HateWords.Contains(word))
            {
                return word;
            }

            foreach (var ending in new[] { "s", "es", "ed", "ing" })
            {
                if (word.Length > ending.Length
                    && word.EndsWith(ending, StringComparison.Ordinal)
                    && HateWords.Contains(word[..^ending.Length]))
                {
                    return word[..^ending.Length];
                }
            }

            foreach (var stem in HateStems)
            {
                if (word.StartsWith(stem, StringComparison.Ordinal))
                {
                    return stem;
                }
            }
        }

        var spaced = " " + string.Join(" ", words) + " ";
        var allowedSpaced = " " + string.Join(" ", Word.Matches(allowed).Select(m => m.Value)) + " ";

        foreach (var phrase in HatePhrases)
        {
            var padded = " " + Fold(phrase) + " ";

            if (spaced.Contains(padded, StringComparison.Ordinal) && !allowedSpaced.Contains(padded, StringComparison.Ordinal))
            {
                return phrase;
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="text"/> with every swear starred after its first
    /// letter - "fucking" shows as "f******". For screens only: what is
    /// SAID is never masked.
    /// </summary>
    /// <param name="keep">
    /// Words never starred, whatever they are - the drivers' names, so a
    /// driver called Dick or Hell shows as himself. Case is ignored.
    /// </param>
    public static string Masked(string text, IEnumerable<string>? keep = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var kept = keep is null
            ? null
            : new HashSet<string>(keep.Where(w => !string.IsNullOrWhiteSpace(w)), StringComparer.OrdinalIgnoreCase);

        return Swears.Replace(text, m =>
            kept is not null && kept.Contains(m.Value)
                ? m.Value
                : m.Value[0] + new string('*', m.Value.Length - 1));
    }

    /// <summary>Every word of every name - what <see cref="Masked"/> keeps for a list of drivers.</summary>
    public static IEnumerable<string> NameWords(IEnumerable<string?> names) =>
        names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => Regex.Split(n!, @"[^\p{L}\p{N}']+"))
            .Where(w => w.Length > 0);

    /// <summary>Lower case, look-alike digits and symbols as letters, a masked letter as a wildcard-free gap.</summary>
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);

        foreach (var c in text.ToLowerInvariant())
        {
            folded.Append(c switch
            {
                '0' => 'o',
                '1' or '!' or '|' => 'i',
                '3' => 'e',
                '4' or '@' => 'a',
                '5' or '$' => 's',
                '7' => 't',
                '*' => 'i',
                _ => c,
            });
        }

        return folded.ToString();
    }

    /// <summary>Runs of single letters joined up: "k i k e" read as "kike".</summary>
    private static IEnumerable<string> SquashedSingles(List<string> words)
    {
        var run = new StringBuilder();

        foreach (var word in words.Append(string.Empty))
        {
            if (word.Length == 1)
            {
                run.Append(word);
                continue;
            }

            // Every stretch of the run, not only all of it: in "like a
            // f.a.g" the "a" before it is a single letter too.
            var letters = run.ToString();

            for (var from = 0; from < letters.Length; from++)
            {
                for (var length = 3; from + length <= letters.Length; length++)
                {
                    yield return letters.Substring(from, length);
                }
            }

            run.Clear();
        }
    }
}
