using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// Keeps the field from being read out twice before a race (19 Oct 2026).
///
/// The driver introductions roll through the grid one by one, and two things
/// could take the room through it again:
///
///   THE OPENING, when the model lists the field anyway. Its prompt says not
///   to when introductions follow (CommentaryService.BuildRoundIntroAsync,
///   introductionsFollow) - but a prompt is a request, the opening may have
///   been written ahead, and a small model on the race PC follows it least
///   of all. So the opening is held to it after it is written: a sentence
///   that names three or more of the drivers is a roll call, and goes.
///
///   THE INTRODUCTIONS THEMSELVES, when the model reaches the end of the
///   grid and starts again - "...and Pat on the back row. So that's Sam on
///   pole, Bill alongside..." - or loops back to the first driver part-way.
///   Cut where the second pass starts.
///
/// Matching is by whole word, ignoring case, on the name the voice was
/// given and on its first word alone (models say "Sam" for "Sam Smith"),
/// unless that first word is shared by two drivers.
///
/// Pure, so every rule is a table test.
/// </summary>
internal static class IntroRepeats
{
    /// <summary>A sentence naming this many drivers or more is a roll call.</summary>
    internal const int RollCallNames = 3;

    /// <summary>Sentence ends: after . ! ? (and any closing quote or bracket), before white space.</summary>
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?][""'”’)\]]*)\s+", RegexOptions.Compiled);

    /// <summary>
    /// The opening with any roll call of the field taken out. Null when
    /// nothing but roll call was left - the caller then says the race in a
    /// plain line of its own rather than read the field twice.
    /// </summary>
    internal static string? WithoutRollCall(string opening, IReadOnlyList<string> driverNames, List<string>? notes = null)
    {
        var text = (opening ?? string.Empty).Trim();
        var drivers = Matchers(driverNames);

        if (text.Length == 0 || drivers.Count < RollCallNames)
        {
            return text.Length == 0 ? null : text;
        }

        var kept = new List<string>();
        var dropped = 0;

        foreach (var sentence in Sentences(text))
        {
            if (Named(sentence, drivers).Count >= RollCallNames)
            {
                dropped++;
                continue;
            }

            kept.Add(sentence);
        }

        if (dropped == 0)
        {
            return text;
        }

        notes?.Add(dropped == 1
            ? "a sentence listing the field was taken out (the driver introductions follow)"
            : $"{dropped} sentences listing the field were taken out (the driver introductions follow)");

        return kept.Count == 0 ? null : string.Join(" ", kept);
    }

    /// <summary>
    /// The introductions cut where they start through the grid a second
    /// time: the first sentence, once every driver has been introduced,
    /// that names any of them again; or, before then, a sentence that goes
    /// back to the first driver introduced after at least
    /// <see cref="RollCallNames"/> others have been. What follows a cut is
    /// dropped too, apart from a closing line that names nobody ("Let's go
    /// racing!").
    /// </summary>
    internal static string WithoutSecondPass(string introductions, IReadOnlyList<string> gridNames, List<string>? notes = null)
    {
        var text = (introductions ?? string.Empty).Trim();
        var drivers = Matchers(gridNames);

        if (text.Length == 0 || drivers.Count < 2)
        {
            return text;
        }

        var sentences = Sentences(text);
        var seen = new HashSet<int>();

        // Who has had a sentence of their own, as against being named in
        // somebody else's ("On pole, Sam Smith, two tenths clear of Bill
        // Jones."): of the drivers a sentence names, the one highest up the
        // grid who has not had one yet.
        var own = new HashSet<int>();
        int? first = null;
        var cutAt = -1;

        // A ROLL CALL IN FRONT OF THEM (19 Oct 2026): "Annie took pole from
        // Al, with Terry third." and then every driver one by one - the
        // front of the field twice. A sentence naming three or more drivers
        // before anybody has been introduced is a list, not an
        // introduction, and goes.
        var leading = new HashSet<int>();

        for (var i = 0; i < sentences.Count; i++)
        {
            var named = Named(sentences[i], drivers);

            if (named.Count == 0)
            {
                continue;
            }

            if (seen.Count == 0 && named.Count >= RollCallNames)
            {
                leading.Add(i);
                continue;
            }

            // Everybody named - but a sentence about one driver who has only
            // been named in passing so far is their introduction, not a
            // second pass. Cut, it cost the last driver theirs whenever an
            // earlier line mentioned them.
            var allIntroduced = seen.Count >= drivers.Count
                                && named.Count(d => !own.Contains(d)) != 1;
            var backToTheStart = first is { } opener
                                 && named.Contains(opener)
                                 && seen.Count > RollCallNames
                                 && !named.Any(d => !seen.Contains(d));

            if (allIntroduced || backToTheStart)
            {
                cutAt = i;
                break;
            }

            first ??= named[0];

            foreach (var driver in named)
            {
                seen.Add(driver);
            }

            var introduced = named.FindIndex(d => !own.Contains(d));

            if (introduced >= 0)
            {
                own.Add(named[introduced]);
            }
        }

        if (leading.Count > 0)
        {
            notes?.Add(leading.Count == 1
                ? "a sentence listing the front of the field before the introductions was taken out"
                : $"{leading.Count} sentences listing the field before the introductions were taken out");
        }

        if (cutAt < 0)
        {
            return leading.Count == 0
                ? text
                : string.Join(" ", sentences.Where((_, i) => !leading.Contains(i)));
        }

        var kept = sentences.Take(cutAt).Where((_, i) => !leading.Contains(i)).ToList();

        // A sign-off after the repeat, with no names in it, is kept: it is
        // the end of the piece, not more of the list.
        var last = sentences[^1];

        if (sentences.Count - 1 > cutAt && Named(last, drivers).Count == 0)
        {
            kept.Add(last);
        }

        notes?.Add("the second time through the grid was taken out");

        return string.Join(" ", kept);
    }

    /// <summary>
    /// The introductions with every driver on the grid in them (11 Nov
    /// 2026). A driver the words never name - their sentence cut by the
    /// figures check, or skipped by the model - gets a plain line of the
    /// app's own ("On pole, Al Gorithim."), put in grid order: in front of
    /// the first sentence naming a driver further down the grid, or else
    /// before a closing line that names nobody. Name and position only, so
    /// there is nothing in it to check.
    ///
    /// WHY: the figures check cut the pole-sitter's sentence for a
    /// misread time, and the room heard "Alongside him on the front row..."
    /// with nobody on pole. Nothing cut from the introductions should cost a
    /// driver their introduction.
    /// </summary>
    internal static string WithEveryDriver(string introductions, IReadOnlyList<IntroDriver> grid, List<string>? notes = null)
    {
        var text = (introductions ?? string.Empty).Trim();

        if (text.Length == 0 || grid is not { Count: > 0 })
        {
            return text;
        }

        var drivers = Matchers(grid.Select(d => d.SpokenName).ToList());

        // Matchers drops blank and repeated names - only fill in when every
        // driver has a pattern of their own, so index means grid place.
        if (drivers.Count != grid.Count)
        {
            return text;
        }

        var sentences = Sentences(text);
        var namedIn = sentences.Select(s => Named(s, drivers)).ToList();
        var named = new HashSet<int>(namedIn.SelectMany(n => n));
        var missing = Enumerable.Range(0, grid.Count).Where(d => !named.Contains(d)).ToList();

        if (missing.Count == 0)
        {
            return text;
        }

        // Where the field ends: before any closing sentences that name nobody.
        var end = sentences.Count;

        while (end > 0 && namedIn[end - 1].Count == 0)
        {
            end--;
        }

        if (end == 0)
        {
            // Nobody named at all - not introductions to repair.
            return text;
        }

        var inserts = new SortedDictionary<int, List<string>>();

        foreach (var d in missing)
        {
            var at = Enumerable.Range(0, end).FirstOrDefault(i => namedIn[i].Any(n => n > d), end);

            if (!inserts.TryGetValue(at, out var list))
            {
                inserts[at] = list = new List<string>();
            }

            list.Add(PlainIntroduction(grid[d], d == grid.Count - 1));
        }

        var result = new List<string>();

        for (var i = 0; i <= sentences.Count; i++)
        {
            if (inserts.TryGetValue(i, out var lines))
            {
                result.AddRange(lines);
            }

            if (i < sentences.Count)
            {
                result.Add(sentences[i]);
            }
        }

        notes?.Add(missing.Count == 1
            ? $"{grid[missing[0]].SpokenName} was never named, so was given a plain introduction"
            : $"{missing.Count} drivers were never named, so were given plain introductions");

        return string.Join(" ", result);
    }

    /// <summary>"On pole, Sam Smith." - a driver's introduction with nothing in it but their name and where they start.</summary>
    private static string PlainIntroduction(IntroDriver driver, bool last)
    {
        var name = driver.SpokenName.Trim();

        return driver.Slot switch
        {
            <= 0 => $"Also in this one, {name}.",
            1 => $"On pole, {name}.",
            2 => $"Alongside on the front row, {name}.",
            _ when last => $"And starting {LeadTracker.SpokenOrdinal(driver.Slot)}, {name}.",
            _ => $"Starting {LeadTracker.SpokenOrdinal(driver.Slot)}, {name}.",
        };
    }

    /// <summary>One driver's patterns: the full name, and the first word when no other driver has it.</summary>
    private static List<Regex[]> Matchers(IReadOnlyList<string> names)
    {
        var clean = (names ?? Array.Empty<string>())
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var firstWords = clean
            .Select(FirstWord)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return clean.Select(name =>
        {
            var patterns = new List<Regex> { Word(name) };
            var firstWord = FirstWord(name);

            // "Sam" for "Sam Smith" - a plain first and last name only, so an
            // announcer name like "The Flying Dutchman" never becomes "The";
            // not a first word two drivers share, not one that is a common
            // word at the start of a sentence (NotAName), and matched as
            // capitalised, as a name is written.
            if (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 2
                && firstWord.Length >= 2
                && char.IsUpper(firstWord[0])
                && !NotAName.Contains(firstWord)
                && firstWords[firstWord] == 1)
            {
                patterns.Add(Word(firstWord, ignoreCase: false));
            }

            return patterns.ToArray();
        }).ToList();
    }

    private static string FirstWord(string name)
    {
        var space = name.IndexOf(' ');
        return space < 0 ? name : name[..space];
    }

    /// <summary>First words that are a title or a word of the language, never a first name to match on its own.</summary>
    private static readonly HashSet<string> NotAName = new(StringComparer.OrdinalIgnoreCase)
    {
        "The", "Mr", "Mrs", "Ms", "Miss", "Dr", "Big", "Little", "Young", "Old", "Captain", "King", "Lord", "Sir", "Team",

        // Two-letter first names count now ("Al" for "Al Gorithim", 19 Oct
        // 2026) - so not the two-letter words that start a sentence.
        "An", "As", "At", "Be", "By", "Do", "Go", "He", "If", "In", "Is", "It", "Me", "My", "No", "Of", "Oh", "On", "Or",
        "So", "To", "Up", "Us", "We",
    };

    private static Regex Word(string text, bool ignoreCase = true) =>
        new($@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(text)}(?![\p{{L}}\p{{N}}])",
            (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);

    /// <summary>Which drivers (by index) a sentence names, in the order of the list.</summary>
    private static List<int> Named(string sentence, List<Regex[]> drivers)
    {
        var named = new List<int>();

        for (var d = 0; d < drivers.Count; d++)
        {
            if (drivers[d].Any(p => p.IsMatch(sentence)))
            {
                named.Add(d);
            }
        }

        return named;
    }

    private static List<string> Sentences(string text) =>
        SentenceEnd.Split(text).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
}
