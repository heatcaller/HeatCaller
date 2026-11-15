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
    /// Everyday words a slur's ending would otherwise catch (15 Nov 2026,
    /// bug hunt 21): "spices", "spiced" and "spicing" all lose their ending
    /// to "spic", so "that spices things up" dropped a whole recap. Checked
    /// before the endings come off; "spics" is still caught.
    /// </summary>
    private static readonly HashSet<string> OrdinaryWords = new(StringComparer.Ordinal)
    {
        "spices", "spiced", "spicing",
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

            if (OrdinaryWords.Contains(word))
            {
                continue;
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

        var kept = KeepSet(keep);

        return Swears.Replace(text, m =>
            kept is not null && kept.Contains(m.Value)
                ? m.Value
                : m.Value[0] + new string('*', m.Value.Length - 1));
    }

    /// <summary>
    /// Swears used as an intensifier - "is fucking stuck", "a damn good
    /// lap" - which come out of a line leaving a sentence that still reads.
    /// "Damn it" is not one: taking "damn" leaves "it".
    /// </summary>
    private static readonly Regex Intensifier = new(
        @"\b(?:mother)?f+u+c+k(?:ing|in')(?!\w)\s+(?=[a-z])|\b(?:god)?damn(?:ed)?\s+(?!it\b)(?=[a-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Words = new(@"[\p{L}\p{N}']+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>How many swears are in <paramref name="text"/>, not counting a kept word (a driver's name).</summary>
    public static int SwearCount(string? text, IEnumerable<string>? keep = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var kept = KeepSet(keep);

        return Swears.Matches(text).Count(m => kept is null || !kept.Contains(m.Value));
    }

    /// <summary>
    /// <paramref name="written"/> brought within the Sailor-mouth dial (15
    /// Nov 2026): at most <paramref name="perLine"/> swears for every
    /// <see cref="CommentaryTones.WordsPerLine"/> words.
    ///
    /// Over it, the spare intensifiers come out first, the last ones first -
    /// "Bill is fucking stuck" becomes "Bill is stuck", which still says the
    /// race. A short line still over after that is refused (null): what is
    /// left needs the swears it has to make sense, and a line the club did
    /// not ask for is not said. A long piece - the recap show, an interview -
    /// is kept with what could be taken out taken out, as one sentence too
    /// many is not worth four minutes of show.
    /// </summary>
    /// <param name="keep">Words that are never counted - the facts the line was written from, so a driver called Dick is not a swear.</param>
    public static string? WithinSwears(string? written, int perLine, IEnumerable<string>? keep = null)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return written;
        }

        var kept = KeepSet(keep);
        var words = Words.Matches(written).Count;
        var allowed = Math.Max(0, perLine) * Math.Max(1, (int)Math.Ceiling(words / (double)CommentaryTones.WordsPerLine));
        var text = written;

        while (SwearCount(text, kept) > allowed)
        {
            var spare = Intensifier.Matches(text)
                .Where(m => kept is null || !kept.Contains(m.Value.Trim()))
                .LastOrDefault();

            if (spare is null)
            {
                break;
            }

            text = SentenceCase(text.Remove(spare.Index, spare.Length), spare.Index);
        }

        if (SwearCount(text, kept) <= allowed)
        {
            return text;
        }

        return words <= CommentaryTones.WordsPerLine * 3 / 2 ? null : text;
    }

    /// <summary>The letter at <paramref name="at"/> capitalised when it now starts a sentence - "Fucking hell" with the first word gone reads "Hell".</summary>
    private static string SentenceCase(string text, int at)
    {
        if (at >= text.Length || !char.IsLower(text[at]))
        {
            return text;
        }

        var before = text[..at].TrimEnd();

        return before.Length == 0 || before[^1] is '.' or '!' or '?' or '"' or ':'
            ? text[..at] + char.ToUpperInvariant(text[at]) + text[(at + 1)..]
            : text;
    }

    private static HashSet<string>? KeepSet(IEnumerable<string>? keep) =>
        keep switch
        {
            null => null,
            HashSet<string> set when set.Comparer.Equals(StringComparer.OrdinalIgnoreCase) => set,
            _ => new HashSet<string>(keep.Where(w => !string.IsNullOrWhiteSpace(w)), StringComparer.OrdinalIgnoreCase),
        };

    /// <summary>
    /// Every word of every proper name the club typed that a shown line may
    /// carry (15 Nov 2026, bug hunt 21) - what <see cref="Masked"/> and the
    /// Sailor-mouth dial keep. It used to be the drivers' names alone, so a
    /// sponsor "Dick's Sporting Goods", a team "Hell Racing" or a recap host
    /// called Dick Johnson came up starred on every screen, in every tone,
    /// and with Sailor-mouth on counted as a swear against the dial.
    ///
    /// From each driver: their name and announcer name, team, sponsors,
    /// classes, mentor and their own crew's names. From the club, when
    /// <paramref name="settings"/> is given: <see cref="ClubNames"/>. Plus
    /// anything in <paramref name="more"/> (tonight's sponsors).
    /// </summary>
    public static IEnumerable<string> ProperNameWords(
        IEnumerable<DriverProfile> profiles,
        AppSettings? settings = null,
        IEnumerable<string?>? more = null) =>
        NameWords(profiles
            .SelectMany(p => new[] { p.DriverName, p.AnnouncerName, p.Team, p.Sponsors, p.Mentor, p.CrewPrincipalName, p.CrewTechName }
                .Concat(p.AllClasses))
            .Concat(settings is null ? Enumerable.Empty<string?>() : ClubNames(settings))
            .Concat(more ?? Enumerable.Empty<string?>()));

    /// <summary>The club's own cast and show names: the announcer, recap host, co-host, mechanic, Team Principal, honours reader, the show and the track.</summary>
    public static IEnumerable<string?> ClubNames(AppSettings settings) =>
        new[]
        {
            settings.RecapAnnouncerName, settings.RecapHostName, settings.CommentaryCoHostName, settings.RecapMechanicName,
            settings.PrincipalName, settings.RecapHonoursName, settings.RecapShowName, settings.LastRaceControlTrackName,
        };

    /// <summary>Every word of every name - what <see cref="Masked"/> keeps for a list of drivers.</summary>
    public static IEnumerable<string> NameWords(IEnumerable<string?> names) =>
        names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => Regex.Split(n!, @"[^\p{L}\p{N}']+"))
            .Where(w => w.Length > 0);

    /// <summary>
    /// Lower case, look-alike digits and symbols as letters, a masked letter as a wildcard-free gap.
    ///
    /// A symbol is read as a letter only when a letter or digit follows it
    /// (15 Nov 2026, bug hunt 21) - "k!ke", "f@g", "n*gger", "$pic". One at
    /// the END of a word is punctuation and is left alone: "retard!" used to
    /// fold to "retardi", which is no word on the list, so an exclaimed slur
    /// was said; and "What a win for Sam Pak!" folded to "paki", so a driver
    /// called Pak lost every line that ended on his name.
    /// </summary>
    private static string Fold(string text)
    {
        var lower = text.ToLowerInvariant();
        var folded = new StringBuilder(lower.Length);

        for (var i = 0; i < lower.Length; i++)
        {
            var c = lower[i];

            folded.Append(c switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                '!' or '|' or '*' when LetterFollows(lower, i) => 'i',
                '@' when LetterFollows(lower, i) => 'a',
                '$' when LetterFollows(lower, i) => 's',
                _ => c,
            });
        }

        return folded.ToString();
    }

    /// <summary>True when, past any more look-alike symbols, the next character after <paramref name="at"/> is a letter or digit - the symbol sits inside a word.</summary>
    private static bool LetterFollows(string text, int at)
    {
        for (var i = at + 1; i < text.Length; i++)
        {
            if (text[i] is '!' or '|' or '*' or '@' or '$')
            {
                continue;
            }

            return char.IsLetterOrDigit(text[i]);
        }

        return false;
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
