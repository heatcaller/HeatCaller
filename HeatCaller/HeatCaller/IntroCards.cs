using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>One driver's card on the Big Screen while they are being introduced.</summary>
/// <param name="Transponder">Who.</param>
/// <param name="SpokenName">The name the voice was given - what is looked for in the words.</param>
/// <param name="ScreenName">The name the card shows - the driver's real name.</param>
/// <param name="PhotoFileName">Their stored photo, or empty for none - or for a driver whose photo is kept off the Big Screen (DriverProfile.NoPhotoOnBigScreen).</param>
/// <param name="Number">Racing number, or empty.</param>
/// <param name="Hometown">Home club or town, or empty.</param>
/// <param name="Slot">Grid slot, 1 for pole - 0 for a race with no grid.</param>
public sealed record IntroCard(
    string Transponder,
    string SpokenName,
    string ScreenName,
    string PhotoFileName = "",
    string Number = "",
    string Hometown = "",
    int Slot = 0);

/// <summary>
/// Where in the driver introductions each driver's card goes up (1 Nov 2026,
/// asked for by Asher: "during driver intro I want the name and pic of the
/// driver displayed on the big screen").
///
/// THE INTRODUCTIONS ARE ONE LINE. The model writes the whole grid as one
/// piece (CommentaryService.BuildDriverIntroductionsAsync) and the announcer
/// says it as one line, so nothing outside the words says when the voice
/// gets to each driver. This finds it IN the words: where each driver is
/// first named as the one being introduced, as a place in the text, which
/// IntroOnAir turns into a time from the line's real length once it plays.
///
/// AS THE ONE BEING INTRODUCED, NOT JUST NAMED. The pole-sitter's line often
/// names second place too ("Sam takes pole, a tenth clear of Bill"), because
/// the prompt asks for the margin there. Bill's card must wait for Bill's own
/// line. So the drivers are taken in grid order - the order the model is told
/// to introduce them in - and a driver named in passing in someone else's
/// sentence only gets their card there when nothing later names them again.
/// Someone else's sentence is one that names an earlier driver first, or
/// one that opens with "He", "She" or "They" once a card is up (bug hunt 24).
///
/// A card goes up where its driver's sentence STARTS ("From pole, with the
/// fastest lap of the night, it's Sam Parker"), so the face is there as the
/// voice starts on them, not three seconds in. A second driver introduced in
/// the same sentence ("Sam on pole, Bill alongside") goes up at their name.
///
/// A driver the model never named gets no card, rather than a guess at
/// where they might have been: a wrong face on the wall is worse than none.
///
/// Matching is IntroRepeats': by whole word, on the name the voice was given
/// and on its first word alone when no other driver shares it - and here on
/// the surname alone too, which a broadcast also uses ("Parker on pole").
///
/// Pure, so every rule is a table test.
/// </summary>
internal static class IntroCards
{
    /// <summary>Sentence ends: after . ! ? (and any closing quote or bracket), before white space.</summary>
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?][""'”’)\]]*)\s+", RegexOptions.Compiled);

    /// <summary>One placed card: which driver (an index into the list given), and where in the text it goes up.</summary>
    internal readonly record struct Placement(int Driver, int At);

    /// <summary>
    /// Where each driver's card goes up in <paramref name="text"/>, in the
    /// order they come up. <paramref name="spokenNames"/> is the grid in
    /// order; <paramref name="screenNames"/>, when given, are looked for
    /// too (the model is handed the spoken name, but a real name is never a
    /// wrong match). Drivers never named are left out.
    /// </summary>
    internal static IReadOnlyList<Placement> Plan(string? text, IReadOnlyList<string> spokenNames, IReadOnlyList<string>? screenNames = null)
    {
        var words = text ?? string.Empty;
        var count = spokenNames?.Count ?? 0;

        if (words.Trim().Length == 0 || count == 0)
        {
            return Array.Empty<Placement>();
        }

        var matchers = Matchers(spokenNames!, screenNames);
        var sentences = Sentences(words);

        // Every sentence's matches, per driver: the first place each driver
        // is named in it, or -1.
        var named = new int[sentences.Count, count];

        for (var s = 0; s < sentences.Count; s++)
        {
            for (var d = 0; d < count; d++)
            {
                named[s, d] = FirstMatch(words, sentences[s], matchers[d]);
            }
        }

        bool NamedFrom(int driver, int sentence)
        {
            for (var s = sentence; s < sentences.Count; s++)
            {
                if (named[s, driver] >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        var placedAt = new int?[count];
        var next = 0;

        // Skips the drivers the words never reach from here - so one the
        // model left out never holds up everybody behind them.
        void SkipUnnamed(int fromSentence)
        {
            while (next < count && (placedAt[next] is not null || !NamedFrom(next, fromSentence)))
            {
                next++;
            }
        }

        // Whether a sentence is still about a driver whose card is already
        // up: it names one of them before the driver due, or it opens with
        // "He" / "She" / "They" once a card is up. Names alone cannot tell
        // more than that, so "Alongside him, Bill Hargrove." still counts as
        // Bill's own line.
        bool StillOnAnEarlierDriver(Sentence sentence, int index, int dueAt)
        {
            var anyPlaced = false;

            for (var d = 0; d < count; d++)
            {
                if (placedAt[d] is null)
                {
                    continue;
                }

                anyPlaced = true;

                if (named[index, d] >= 0 && named[index, d] < dueAt)
                {
                    return true;
                }
            }

            return anyPlaced && OpensWithAPronoun(words, sentence);
        }

        // FIRST PASS, in grid order: each sentence introduces the driver due
        // next when it names them - and then the one after, and so on, for
        // "Sam on pole, Bill alongside".
        for (var s = 0; s < sentences.Count; s++)
        {
            SkipUnnamed(s);

            var placedHere = false;

            while (next < count && named[s, next] >= 0)
            {
                var at = named[s, next];

                if (!placedHere)
                {
                    // WHY (23 Nov 2026, bug hunt 24): the pass-by check below
                    // only ran when another card had gone up in the same
                    // sentence, so a passing mention in a sentence of its own
                    // ("From pole, it's Sam Parker. He was a tenth clear of
                    // Bill Hargrove. Starting second, Bill Hargrove.") put
                    // Bill's face up while the voice was still on Sam.
                    if (StillOnAnEarlierDriver(sentences[s], s, at) && NamedFrom(next, s + 1))
                    {
                        break;
                    }

                    // Their sentence's start: the voice is starting on them.
                    placedAt[next] = sentences[s].Start;
                    placedHere = true;
                }
                else if (NamedFrom(next, s + 1))
                {
                    // Named in passing in the driver before's sentence, and
                    // introduced properly later: theirs is the later one.
                    break;
                }
                else
                {
                    placedAt[next] = at;
                }

                next++;
                SkipUnnamed(s);
            }
        }

        // SECOND PASS: a driver the model took out of order - named, but
        // never when they were due. Their first naming sentence, from its
        // start when no other card goes up in it, otherwise from their name.
        for (var d = 0; d < count; d++)
        {
            if (placedAt[d] is not null)
            {
                continue;
            }

            for (var s = 0; s < sentences.Count; s++)
            {
                if (named[s, d] < 0)
                {
                    continue;
                }

                var taken = placedAt.Any(p => p is { } at && at >= sentences[s].Start && at < sentences[s].End);
                placedAt[d] = taken ? named[s, d] : sentences[s].Start;
                break;
            }
        }

        return placedAt
            .Select((at, d) => (at, d))
            .Where(p => p.at is not null)
            .Select(p => new Placement(p.d, p.at!.Value))
            .OrderBy(p => p.At)
            .ThenBy(p => p.Driver)
            .ToList();
    }

    /// <summary>
    /// How many of <paramref name="spokenNames"/> <paramref name="text"/>
    /// names at all - what tells the driver introductions apart from any
    /// other line the announcer says (IntroOnAir.Heard).
    /// </summary>
    internal static int NamedCount(string? text, IReadOnlyList<string> spokenNames, IReadOnlyList<string>? screenNames = null)
    {
        var words = text ?? string.Empty;

        if (words.Trim().Length == 0 || spokenNames is not { Count: > 0 })
        {
            return 0;
        }

        var whole = new Sentence(0, words.Length);

        return Matchers(spokenNames, screenNames).Count(m => FirstMatch(words, whole, m) >= 0);
    }

    /// <summary>The grid slot as the card says it: "POLE", "STARTING 2ND", or nothing for a race with no grid.</summary>
    internal static string SlotLine(int slot) => slot switch
    {
        <= 0 => string.Empty,
        1 => "POLE",
        _ => $"STARTING {Ordinal(slot)}",
    };

    /// <summary>The line under the name: "#7 · Riverside", with whichever parts the driver has.</summary>
    internal static string DetailLine(string? number, string? hometown)
    {
        var parts = new List<string>();
        var n = (number ?? string.Empty).Trim().TrimStart('#').Trim();

        if (n.Length > 0)
        {
            parts.Add("#" + n);
        }

        if (!string.IsNullOrWhiteSpace(hometown))
        {
            parts.Add(hometown.Trim());
        }

        return string.Join("  ·  ", parts);
    }

    private static string Ordinal(int position)
    {
        if (position % 100 is >= 11 and <= 13)
        {
            return $"{position}TH";
        }

        return (position % 10) switch
        {
            1 => $"{position}ST",
            2 => $"{position}ND",
            3 => $"{position}RD",
            _ => $"{position}TH",
        };
    }

    // ==================== Matching ====================

    private readonly record struct Sentence(int Start, int End);

    /// <summary>The sentences of <paramref name="text"/>, as where each starts and ends in it.</summary>
    private static List<Sentence> Sentences(string text)
    {
        var sentences = new List<Sentence>();
        var start = 0;

        foreach (Match gap in SentenceEnd.Matches(text))
        {
            Add(start, gap.Index);
            start = gap.Index + gap.Length;
        }

        Add(start, text.Length);
        return sentences;

        void Add(int from, int to)
        {
            // Leading white space is not where the voice starts.
            while (from < to && char.IsWhiteSpace(text[from]))
            {
                from++;
            }

            if (to > from)
            {
                sentences.Add(new Sentence(from, to));
            }
        }
    }

    /// <summary>A sentence that opens on a driver already spoken of: "He was a tenth clear...", "Her best lap...".</summary>
    private static readonly Regex PronounOpening = new(@"\G(?:He|She|They|His|Her|Their)(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="sentence"/> opens with he, she, they, his, her or their.</summary>
    private static bool OpensWithAPronoun(string text, Sentence sentence) =>
        PronounOpening.Match(text, sentence.Start, sentence.End - sentence.Start).Success;

    /// <summary>Where in <paramref name="text"/>, inside <paramref name="sentence"/>, a driver is first named - or -1.</summary>
    private static int FirstMatch(string text, Sentence sentence, Regex[] patterns)
    {
        var first = -1;

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(text, sentence.Start, sentence.End - sentence.Start);

            if (match.Success && (first < 0 || match.Index < first))
            {
                first = match.Index;
            }
        }

        return first;
    }

    /// <summary>
    /// Each driver's patterns: the spoken name and the screen name whole,
    /// and the first word and the surname of a plain two-word name when no
    /// other driver has that word - capitalised, as a name is written.
    /// </summary>
    private static List<Regex[]> Matchers(IReadOnlyList<string> spokenNames, IReadOnlyList<string>? screenNames)
    {
        var drivers = spokenNames
            .Select((spoken, i) => new[] { spoken, screenNames is not null && i < screenNames.Count ? screenNames[i] : null }
                .Select(n => (n ?? string.Empty).Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList())
            .ToList();

        // How many drivers each single word could stand for.
        var wordUse = drivers
            .SelectMany(names => names.SelectMany(PlainParts).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return drivers.Select(names =>
        {
            var patterns = names.Select(n => Word(n)).ToList();

            foreach (var part in names.SelectMany(PlainParts).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (part.Length >= 2
                    && char.IsUpper(part[0])
                    && !NotAName.Contains(part)
                    && wordUse[part] == 1)
                {
                    patterns.Add(Word(part, ignoreCase: false));
                }
            }

            return patterns.ToArray();
        }).ToList();
    }

    /// <summary>The first word and the surname of a plain two-word name; nothing for any other.</summary>
    private static IEnumerable<string> PlainParts(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 2 ? parts : Enumerable.Empty<string>();
    }

    /// <summary>Words that are a title or a word of the language, never a name to match on its own - IntroRepeats' list.</summary>
    private static readonly HashSet<string> NotAName = new(StringComparer.OrdinalIgnoreCase)
    {
        "The", "Mr", "Mrs", "Ms", "Miss", "Dr", "Big", "Little", "Young", "Old", "Captain", "King", "Lord", "Sir", "Team",
        "An", "As", "At", "Be", "By", "Do", "Go", "He", "If", "In", "Is", "It", "Me", "My", "No", "Of", "Oh", "On", "Or",
        "So", "To", "Up", "Us", "We",
    };

    private static Regex Word(string text, bool ignoreCase = true) =>
        new($@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(text)}(?![\p{{L}}\p{{N}}])",
            (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);
}
