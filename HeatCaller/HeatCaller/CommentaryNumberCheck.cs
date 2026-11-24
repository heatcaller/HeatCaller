using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// Whether every number in a line the AI wrote is one its facts can back.
///
/// WHY (8 Nov 2026)
///
/// The AI writes its lines from facts the app has worked out, and until now
/// nothing checked that the numbers it hands back are the ones it was given.
/// The only guard was a sentence in the system prompt - "Never invent names,
/// times, positions or events" - and a model that has been told a thing has
/// not thereby done it. A club has to be able to trust every number the
/// announcer says: a lap time the screen does not show, or "eleven laps
/// left" with nine to go, is the PA getting the race wrong in front of the
/// people who were watching it. So a line is now read for its figures, and
/// a line with a figure its facts cannot back is DROPPED - never repaired,
/// because a repaired line is the app guessing at what the model meant.
///
/// WHAT COUNTS AS BACKED. A figure is backed when it is one of the facts'
/// own figures, or the sum or difference of two of them OF THE SAME KIND
/// (below) - "losing seven tenths a lap" is two lap times subtracted, "nine
/// laps left" is the limit less the leader's lap. Within a tolerance for a
/// figure with a decimal in it (0.05 either way), exactly for a whole number - or
/// a whole number that one of the facts' own decimals rounds to ("about
/// three seconds" for 2.8s). Hedges such as "about", "nearly" and "just
/// over" are not read: only the figure matters.
///
/// ONLY LIKE WITH LIKE (8 Nov 2026). Any two facts used to back their sum
/// or difference, so facts of a 1.1s gap and a 6.039s lap backed an
/// invented "four point nine" - 6.039 less 1.1 - which is no quantity in
/// the race at all. Each figure in the facts is now given a KIND from the
/// words round it, and only two of the same kind combine:
///   * TIME - a lap or sector time: seconds, or a decimal near "lap",
///     "best", "fastest". Two times give a gap ("seven-tenths a lap" from
///     two last laps, "a third of a second better" from two bests);
///   * GAP - a margin: near "gap", "behind", "ahead", "margin", "by",
///     "clear", "losing"... Two gaps add up ("Sam is 0.3 on Bill, who is
///     0.5 on Dee");
///   * COUNT - a whole number of laps: near "lap", "laps", "to go",
///     "left". The limit less the laps done is the laps left;
///   * POSITION - P4, 4th, "grid position 4": places gained;
///   * OTHER - anything the words round it do not settle (points, wins,
///     a class's 17.5), which combines only with OTHER, so the counts and
///     points a line used to work out still work out.
/// Never a time with a gap, never a count with a time. A figure used
/// directly is backed whatever its kind - the kinds only decide which
/// pairs make a new number. The parts of a lap time in words ("five",
/// "four", "twenty-five" of "five four twenty-five") and the halves of a
/// class ("17" and "5" of "17.5") back themselves only, never a sum.
///
/// HOW A FIGURE IS READ, on both sides - the facts are mostly digits, but
/// some are already words (a lap time read out by RaceNumbers.LapSpoken):
///   * digits: 5.42, 1.1s, 12, 40%, P4, and milliseconds in the facts
///     ("6039 ms" backs 6039 and 6.039);
///   * numbers in words, any case: nine, forty-two, NINE, one hundred and
///     fifty;
///   * tenths, hundredths, thousandths: "seven-tenths", "three tenths",
///     "a tenth" (0.1);
///   * halves, thirds and quarters OF A SECOND: "half a second" is 0.5, "a
///     third of a second" 0.33, "a second and a half" 1.5. Only of a second:
///     "halfway", "half the field" and "a third straight win" are not
///     figures, because nobody can say from the words what they would be;
///   * a lap time the way it is called, RaceNumbers.LapSpoken's shapes:
///     "five forty-two" (5.42), "five four twenty-five" (5.425), "six oh
///     nine" (6.09), "five oh oh six" (5.006), "five flat";
///   * "point" forms: "four point nine", "point three".
///
/// WHAT IS NEVER A FIGURE: 0, 1 and 2 written as words ("one of the", "two
/// cars" - too common to be a claim); ordinals ("third", "12th", "twenty-
/// first"); years; anything glued to letters ("B6.4", "4WD", "1/8") or to a
/// colon ("7:30"); and a name from the facts that has a number in it ("TLR 22
/// 5.0", "Lucky Seven"), which is taken out of the line before it is read.
///
/// WHEN IN DOUBT, NOT A FIGURE. A dropped line is a moment of silence; a
/// check that drops good lines is one a club turns off. Words that could be
/// a lap time or two separate numbers ("five forty-two") pass if either
/// reading is backed; something that might be a year in words ("twenty
/// twenty-six") is let go; and the check never throws - anything it trips
/// over lets the line through.
/// </summary>
internal static class CommentaryNumberCheck
{
    /// <summary>
    /// Where a sentence ends: a full stop, ! or ? (and any closing quote or
    /// bracket) after a letter, then a space. Not after a digit, so "1. Annie
    /// is on pole" stays one sentence and a time is never split.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex SentenceEnd = new(
        @"(?<=[^\d\s][.!?…][""'”’)\]]?)\s+(?=\S)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A speaker label at the start of a sentence - "HOST:", "**Tech Lead:**",
    /// "Amanda:" - up to three words of letters and a colon.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex SpeakerLabel = new(
        @"^\s*[*_]*\p{Lu}\p{L}*(?: \p{Lu}\p{L}*){0,2}[*_]*\s*:[*_]*",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A speaker label further into a sentence, after the end of a sentence
    /// SentenceEnd did not split at ("...on lap 12. TECH LEAD: ...").
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex InnerSpeakerLabel = new(
        @"[.!?…][""'”’)\]]?\s+[*_]*\p{Lu}\p{L}*(?: \p{Lu}\p{L}*){0,2}[*_]*\s*:",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// <paramref name="answer"/> with only the sentences that carry a figure
    /// the facts cannot back taken out (8 Nov 2026, Asher: "the intro needs
    /// to stay. one wrong number shouldn't kill it"). Null when that would
    /// leave a line of it empty (a driver's introduction, one turn of an
    /// interview) or keep less than half its words - then the line is too
    /// broken to say, and is refused whole as before. The answer itself when
    /// nothing needed cutting. <paramref name="cut"/> lists each sentence
    /// taken out and the figure it was for.
    /// </summary>
    public static string? WithoutUnsupportedSentences(string answer, string? facts, out IReadOnlyList<(string Sentence, string Figure)> cut)
    {
        var removed = new List<(string Sentence, string Figure)>();
        cut = removed;

        if (string.IsNullOrWhiteSpace(answer) || Unsupported(answer, facts) is null)
        {
            return answer;
        }

        var lines = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                kept.Add(line);
                continue;
            }

            var sentences = SentenceEnd.Split(line);
            var keep = new List<string>(sentences.Length);

            // A cut sentence never takes a speaker label with it (10 Nov
            // 2026, bug hunt 16). In an interview "HOST:" or "PRINCIPAL:"
            // is part of the first sentence of its turn, so cutting that
            // sentence used to leave the rest of the turn untagged, and
            // ParseInterview then gave it to the other speaker - the host's
            // question read out in the guest's voice. The label now moves on
            // to the next sentence kept. A turn with nothing kept before the
            // next label (or the line's end) is gone whole, label and all, so
            // nothing of it can land in the wrong voice. A label inside the
            // cut sentence itself (no split after a digit: "...on lap 12.
            // TECH LEAD: ...") refuses the answer whole, as before the 8 Nov
            // repair - there is no telling where that turn's words went.
            string? label = null;

            foreach (var sentence in sentences)
            {
                var lead = SpeakerLabel.Match(sentence);

                if (Unsupported(sentence, facts) is { } figure)
                {
                    removed.Add((sentence.Trim(), figure));

                    if (InnerSpeakerLabel.IsMatch(sentence))
                    {
                        return null;
                    }

                    if (lead.Success)
                    {
                        label = lead.Value.Trim();
                    }
                }
                else if (label is not null && !lead.Success)
                {
                    keep.Add(label + " " + sentence.TrimStart());
                    label = null;
                }
                else
                {
                    keep.Add(sentence);
                    label = null;
                }
            }

            // A line with nothing left is a driver with no introduction, or a
            // question with no answer - too broken to say.
            if (keep.Count == 0)
            {
                return null;
            }

            kept.Add(string.Join(" ", keep));
        }

        var result = string.Join("\n", kept);

        static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        if (Words(result) * 2 < Words(answer))
        {
            return null;
        }

        // A figure only the whole line made unbacked (one split across two
        // sentences): refused whole rather than guessed at.
        return Unsupported(result, facts) is null ? result : null;
    }

    /// <summary>
    /// The first figure in <paramref name="answer"/> that <paramref name="facts"/>
    /// cannot back, as the line words it ("four point nine", "eleven") - or
    /// null when every figure is backed, when there are none, or when the
    /// check could not decide.
    /// </summary>
    public static string? Unsupported(string? answer, string? facts)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        try
        {
            var backing = new Backing(FiguresIn(facts ?? string.Empty, forFacts: true));

            foreach (var figure in FiguresIn(WithoutNames(answer, facts ?? string.Empty), forFacts: false))
            {
                if (!backing.Backs(figure))
                {
                    return figure.Said;
                }
            }

            return null;
        }
        catch (Exception)
        {
            // Never throws, and never drops a line it could not read: a
            // check that fell over is not evidence that the line is wrong.
            return null;
        }
    }

    /// <summary>The text between the facts markers of a prompt, or the whole prompt when it has none.</summary>
    internal static string FactsIn(string prompt, string open = "<facts>", string close = "</facts>")
    {
        var from = prompt.IndexOf(open, StringComparison.Ordinal);
        var to = from >= 0 ? prompt.IndexOf(close, from, StringComparison.Ordinal) : -1;

        return from >= 0 && to > from ? prompt[(from + open.Length)..to] : prompt;
    }

    /// <summary>
    /// One figure read from a line or from the facts.
    /// </summary>
    /// <param name="Said">The words it was read from, for the console.</param>
    /// <param name="Value">What it is worth - always positive.</param>
    /// <param name="Whole">Said as a whole number, which must be matched exactly.</param>
    /// <param name="Step">The precision it was said to: 0.1 for tenths, 0.5 for "and a half".</param>
    internal sealed record Figure(string Said, double Value, bool Whole, double Step)
    {
        /// <summary>The same words read as separate numbers - "five" and "forty-two" for "five forty-two".</summary>
        public IReadOnlyList<Figure> Apart { get; init; } = Array.Empty<Figure>();

        /// <summary>Could be a year said in words ("twenty twenty-six"), so let go if nothing else backs it.</summary>
        public bool MaybeYear { get; init; }

        /// <summary>
        /// Words that can only be one lap time - "six oh nine", or joined by
        /// hyphens, "five-oh-six" - so in an answer they never pass as
        /// separate numbers (<see cref="Apart"/>). 8 Nov 2026: Terry Byte's
        /// 5.096 was recapped as "five-oh-six", and its five and six were
        /// backed by the five cars and Dee Ploy's six laps in front.
        /// </summary>
        public bool Joined { get; init; }

        /// <summary>Said the way a lap time is called ("five oh nine six") - held to its own precision, see BacksValue.</summary>
        public bool LapShaped { get; init; }

        /// <summary>What a FACT's figure is, from the words round it - null until read, and then OTHER. See <see cref="FigureKind"/>.</summary>
        public FigureKind? Kind { get; init; }
    }

    /// <summary>
    /// What a figure in the facts measures, as far as the words round it say
    /// (8 Nov 2026) - only two of the same kind make a sum or a difference
    /// that backs a figure. See the class's own summary.
    /// </summary>
    internal enum FigureKind
    {
        /// <summary>Not settled by the words round it. Combines only with another OTHER.</summary>
        Other,

        /// <summary>A lap or sector time, in seconds.</summary>
        Time,

        /// <summary>A gap or margin between two cars.</summary>
        Gap,

        /// <summary>A whole number of laps.</summary>
        Count,

        /// <summary>A place: P4, 4th, grid position 4.</summary>
        Position,

        /// <summary>A piece of another figure (a lap time's words, a class's halves) - backs itself, never a sum.</summary>
        Part,
    }

    /// <summary>The kind of every figure <see cref="FiguresIn"/> reads from <paramref name="facts"/>, for the tests.</summary>
    internal static IReadOnlyList<(double Value, FigureKind Kind)> KindsIn(string facts) =>
        FiguresIn(facts, forFacts: true).Select(f => (Math.Round(f.Value, 3), f.Kind ?? FigureKind.Other)).ToList();

    // ------------------------------------------------------------------
    // Backing: the facts' figures, and every sum and difference of two
    // ------------------------------------------------------------------

    private sealed class Backing
    {
        /// <summary>More than this and the facts are a whole night's; the first few hundred figures are plenty.</summary>
        private const int MostFigures = 400;

        private readonly List<double> _direct = new();
        private readonly List<FigureKind> _kinds = new();
        private readonly List<double> _all = new();

        public Backing(IEnumerable<Figure> figures)
        {
            var seen = new HashSet<(long, FigureKind)>();

            // The words of a lap time ("five", "four", "twenty-five") are
            // parts of it, and back themselves only - see FigureKind.Part.
            var flat = figures.SelectMany(f => f.Apart
                .Select(part => part with { Kind = FigureKind.Part })
                .Prepend(f));

            foreach (var figure in flat)
            {
                if (_direct.Count >= MostFigures)
                {
                    break;
                }

                var kind = figure.Kind ?? FigureKind.Other;

                if (double.IsFinite(figure.Value) && seen.Add(((long)Math.Round(figure.Value * 1_000_000), kind)))
                {
                    _direct.Add(figure.Value);
                    _kinds.Add(kind);
                }
            }

            _all.AddRange(_direct);

            // Only like with like (8 Nov 2026): a gap and a lap time made
            // "four point nine" out of nothing. See FigureKind.
            for (var i = 0; i < _direct.Count; i++)
            {
                if (_kinds[i] == FigureKind.Part)
                {
                    continue;
                }

                for (var j = i + 1; j < _direct.Count; j++)
                {
                    if (_kinds[j] != _kinds[i])
                    {
                        continue;
                    }

                    // Two places apart is places gained; two places added
                    // up is nothing - P1 and P5 made a "six" of the recap's
                    // top five.
                    if (_kinds[i] != FigureKind.Position)
                    {
                        _all.Add(_direct[i] + _direct[j]);
                    }

                    _all.Add(Math.Abs(_direct[i] - _direct[j]));
                }
            }
        }

        public bool Backs(Figure figure)
        {
            if (BacksValue(figure))
            {
                return true;
            }

            // Words that also read as separate numbers pass when every one
            // of those is backed - or is a 0, 1 or 2, which are never claims.
            if (!figure.Joined && figure.Apart.Count > 0 && figure.Apart.All(part => part.Value <= 2 || BacksValue(part)))
            {
                return true;
            }

            return figure.MaybeYear;
        }

        private bool BacksValue(Figure figure)
        {
            if (figure.Whole)
            {
                // Exactly - a lap count or a position is right or wrong -
                // or a decimal of the facts' own that rounds to it.
                return _all.Any(v => Math.Abs(v - figure.Value) < 1e-6)
                       || _direct.Any(v => Math.Abs(v - Math.Round(v)) > 1e-6
                                           && Math.Abs(Math.Round(v, MidpointRounding.AwayFromZero) - figure.Value) < 1e-6);
            }

            // "A second and a half" is how RaceNumbers.GapSpoken says
            // anything from 1.25 to 1.75, so a half is backed by what rounds
            // to it; everything else within 0.05. Not 5% of the fact as well
            // (8 Nov 2026): on a 6.753 lap that let "a six fifty" through.
            //
            // A lap time called to the hundredth or finer is held to that,
            // within one step either way - rounded or cut short (8 Nov 2026):
            // within 0.05, "five-oh-six" (5.06) passed for Terry Byte's 5.096
            // in the recap. Five oh nine, five ten and five oh nine six still
            // pass. A gap keeps the 0.05: "1.12 seconds" for a 1.1 is a gap
            // the facts rounded, not a lap time misread.
            var near = figure.Step >= 0.5 ? (figure.Step / 2) + 1e-9
                : figure.LapShaped && figure.Step <= 0.01 ? figure.Step + 1e-9
                : 0.05 + 1e-9;

            return _all.Any(v => Math.Abs(v - figure.Value) <= near);
        }
    }

    // ------------------------------------------------------------------
    // Names with numbers in them
    // ------------------------------------------------------------------

    /// <summary>
    /// The line with every name from the facts that has a number in it
    /// taken out - "TLR 22 5.0", "Car 51", "Lucky Seven" - so the number in
    /// a name is never read as a claim, nor run into the figure beside it.
    /// Only ever takes out words that are in the facts, so it can never hide
    /// a figure the facts do not have.
    /// </summary>
    private static string WithoutNames(string answer, string facts)
    {
        foreach (var name in NamesWithNumbers(facts))
        {
            answer = Regex.Replace(
                answer,
                $@"(?<![A-Za-z0-9]){Regex.Escape(name)}(?![A-Za-z0-9])",
                " , ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return answer;
    }

    private static IEnumerable<string> NamesWithNumbers(string facts)
    {
        var names = new List<string>();

        foreach (var phrase in Breaks.Split(Tidy(facts)))
        {
            var tokens = phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Trimmed(t))
                .ToList();

            var run = new List<string>();

            void Close()
            {
                if (run.Count >= 2
                    && (char.IsUpper(run[0][0]) || char.IsDigit(run[0][0]))
                    && run.Any(t => t.Any(char.IsDigit) || IsNumberWord(t))
                    && run.Any(t => char.IsUpper(t[0]) && t.All(c => char.IsLetter(c) || c == '\'' || c == '-') && !IsNumberWord(t)))
                {
                    names.Add(string.Join(" ", run));
                }

                run.Clear();
            }

            foreach (var token in tokens)
            {
                if (token.Length > 0 && (char.IsUpper(token[0]) || token.Any(char.IsDigit)))
                {
                    run.Add(token);
                }
                else
                {
                    Close();
                }
            }

            Close();

            if (names.Count >= 50)
            {
                break;
            }
        }

        // Longest first, so "TLR 22 5.0" is taken out before "TLR 22".
        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length).ToList();
    }

    private static bool IsNumberWord(string token) =>
        token.ToLowerInvariant().Split('-').All(part => SmallOf(part) is not null || TensOf(part) is not null);

    // ------------------------------------------------------------------
    // Reading the figures
    // ------------------------------------------------------------------

    /// <summary>A word of the text, and whether it was joined to the one before by a hyphen.</summary>
    private readonly record struct Word(string Raw, string Low, bool Hyphened);

    private static readonly Regex AudioTags = new(@"\[[^\]\r\n]{0,40}\]", RegexOptions.Compiled);

    private static readonly Regex ThousandsComma = new(@"(?<=[0-9]),(?=[0-9]{3}(?![0-9]))", RegexOptions.Compiled);

    // Where one figure cannot run into the next: punctuation, a dash with
    // space round it, a full stop that is not a decimal point, a colon that
    // is not in a clock time - and the end of a line (8 Nov 2026), so the
    // words of one line of the facts are not read as saying what a figure
    // on the line before it is.
    private static readonly Regex Breaks = new(
        @"[,;!?""()“”–—…\r\n]|\.(?![0-9])|:(?![0-9])|\s-+\s|\s-+$|^-+\s",
        RegexOptions.Compiled);

    private static readonly Regex DigitFigure = new(
        @"^[+\-]?(?:(?<int>[0-9]{1,9})(?:\.(?<frac>[0-9]{1,6}))?|\.(?<frac>[0-9]{1,6}))(?<unit>s|sec|secs|seconds?|ms|%)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Position = new(@"^(?:p|#)(?<n>[0-9]{1,3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DigitOrdinal = new(@"^[0-9]{1,4}(?:st|nd|rd|th)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Clock = new(@"^(?<m>[0-9]{1,3}):(?<s>[0-9]{2})(?:\.[0-9]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AnyDigits = new(@"[0-9]+(?:\.[0-9]+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Spelled = new(@"^[a-z]+(?:-[a-z]+)+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Tidy(string text) =>
        ThousandsComma.Replace(AudioTags.Replace(text.Replace('’', '\''), " , "), string.Empty);

    /// <summary>A token without the quotes, stars and possessive round it.</summary>
    private static string Trimmed(string token)
    {
        token = token.Trim('\'', '"', '`', '*', '_');

        if (token.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
        {
            token = token[..^2];
        }

        return token.Trim('\'');
    }

    /// <summary>Every figure in <paramref name="text"/>. The facts keep everything; a line leaves out what is never a claim.</summary>
    internal static IReadOnlyList<Figure> FiguresIn(string text, bool forFacts)
    {
        var found = new List<Figure>();

        foreach (var phrase in Breaks.Split(Tidy(text)))
        {
            var words = new List<Word>();

            foreach (var raw in phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = Trimmed(raw);

                if (token.Length == 0)
                {
                    continue;
                }

                var low = token.ToLowerInvariant();

                if (Spelled.IsMatch(low))
                {
                    var parts = token.Split('-');

                    for (var p = 0; p < parts.Length; p++)
                    {
                        words.Add(new Word(parts[p], parts[p].ToLowerInvariant(), Hyphened: p > 0));
                    }
                }
                else
                {
                    words.Add(new Word(token, low, Hyphened: false));
                }
            }

            for (var i = 0; i < words.Count;)
            {
                var from = found.Count;
                var took = Math.Max(1, ReadAt(words, i, forFacts, found));

                // A fact's figures are given their kind from the words round
                // them, so only like combines with like (8 Nov 2026).
                if (forFacts && found.Count > from)
                {
                    var kind = KindAt(words, i, took, found[from]);

                    for (var f = from; f < found.Count; f++)
                    {
                        if (found[f].Kind is null)
                        {
                            found[f] = found[f] with { Kind = kind };
                        }
                    }
                }

                i += took;
            }
        }

        return found;
    }

    // ------------------------------------------------------------------
    // What a fact's figure measures (8 Nov 2026) - see FigureKind
    // ------------------------------------------------------------------

    private enum Cue
    {
        None,
        Gap,
        Time,
        Lap,
        Place,
        Else,
    }

    private static readonly HashSet<string> GapWords = new(StringComparer.Ordinal)
    {
        "gap", "gaps", "behind", "ahead", "margin", "margins", "clear", "back", "losing", "gaining", "lost", "gained",
        "closing", "quicker", "faster", "slower", "deficit", "advantage", "interval", "apart", "off", "within",
        "lead", "leads", "leading", "cushion", "difference", "adrift", "between",
    };

    private static readonly HashSet<string> TimeWords = new(StringComparer.Ordinal)
    {
        "best", "fastest", "quickest", "slowest", "pb", "record", "time", "lapped", "clocked", "average",
        "avg", "personal", "sector",
    };

    private static readonly HashSet<string> LapWords = new(StringComparer.Ordinal)
    {
        "lap", "laps", "circuit", "circuits", "go", "left", "remaining", "done", "completed", "tours",
    };

    private static readonly HashSet<string> PlaceWords = new(StringComparer.Ordinal)
    {
        "position", "positions", "place", "places", "grid", "pole",
    };

    // Words that say a number is about something else - points, wins,
    // starts - so a "laps" further off does not claim it.
    private static readonly HashSet<string> ElseWords = new(StringComparer.Ordinal)
    {
        "points", "pts", "pt", "rating", "rated", "percent", "wins", "win", "heats", "heat", "races", "race",
        "starts", "podiums", "titles", "achievements", "drivers", "cars", "minutes", "minute", "mins", "years",
        "nights", "meetings", "mains", "rounds", "round", "seasons", "hours",
    };

    private static readonly HashSet<string> SecondsWords = new(StringComparer.Ordinal)
    {
        "s", "sec", "secs", "second", "seconds", "ms", "millisecond", "milliseconds",
    };

    private static Cue CueOf(string word, bool before, int distance)
    {
        // "by" only right in front: "by 1.1s", "by just 1.1s" - and not the
        // "set by Sam at 5.425s" of a fastest lap.
        if (word == "by")
        {
            return before && distance <= 2 ? Cue.Gap : Cue.None;
        }

        // "times" is a lap time's plural and a count's ("three times") alike: not a cue.
        if (word == "times")
        {
            return Cue.None;
        }

        return GapWords.Contains(word) ? Cue.Gap
            : TimeWords.Contains(word) ? Cue.Time
            : LapWords.Contains(word) ? Cue.Lap
            : PlaceWords.Contains(word) ? Cue.Place
            : ElseWords.Contains(word) ? Cue.Else
            : Cue.None;
    }

    /// <summary>The nearest word round the figure that says what it is - after it first, at each distance.</summary>
    private static Cue NearestCue(List<Word> ws, int i, int took)
    {
        for (var d = 1; d <= 4; d++)
        {
            var after = i + took - 1 + d;

            if (after < ws.Count && CueOf(ws[after].Low, before: false, d) is not Cue.None and var a)
            {
                return a;
            }

            var before = i - d;

            if (before >= 0 && CueOf(ws[before].Low, before: true, d) is not Cue.None and var b)
            {
                return b;
            }
        }

        return Cue.None;
    }

    /// <summary>What the figure read at word <paramref name="i"/> of the facts measures. When in doubt, OTHER.</summary>
    private static FigureKind KindAt(List<Word> ws, int i, int took, Figure first)
    {
        var token = ws[i].Low;

        if (token.StartsWith('p') && Position.IsMatch(token) || DigitOrdinal.IsMatch(token))
        {
            return FigureKind.Position;
        }

        if (token.EndsWith('%'))
        {
            return FigureKind.Other;
        }

        var m = DigitFigure.Match(token);
        var seconds = (m.Success && m.Groups["unit"].Value is { Length: > 0 } unit && unit != "%")
                      || (i + 1 < ws.Count && SecondsWords.Contains(ws[i + 1].Low));
        var cue = NearestCue(ws, i, took);

        if (cue == Cue.Else)
        {
            return FigureKind.Other;
        }

        if (!first.Whole || seconds)
        {
            return cue switch
            {
                Cue.Gap => FigureKind.Gap,
                Cue.Time or Cue.Lap => FigureKind.Time,
                _ => seconds ? FigureKind.Time : FigureKind.Other,
            };
        }

        return cue switch
        {
            Cue.Lap => FigureKind.Count,
            Cue.Place => FigureKind.Position,
            Cue.Gap => FigureKind.Gap,

            // "This one was close", "one of the best": a nought, one or two
            // in words that nothing round it makes a count backs itself and
            // nothing else - 6 drivers and "this one" made a "seven".
            _ when first.Value <= 2 && !token.Any(char.IsDigit) => FigureKind.Part,
            _ => FigureKind.Other,
        };
    }

    /// <summary>Reads whatever figure starts at word <paramref name="i"/>, and says how many words it took.</summary>
    private static int ReadAt(List<Word> ws, int i, bool forFacts, List<Figure> found)
    {
        var w = ws[i].Low;

        if (w.Any(char.IsDigit))
        {
            return ReadDigits(ws, i, forFacts, found);
        }

        // "point three", with no whole number in front. Not "at this point
        // three drivers", where the point is a noun and the three a count.
        if (w == "point" && DigitWords(ws, i + 1) is { } bare
            && !Is(ws, i - 1, PointAsNoun) && !Is(ws, i + 1 + bare.Length, Counted))
        {
            found.Add(new Figure(Said(ws, i, 1 + bare.Length), Parse("0." + bare.Digits), false, Math.Pow(10, -bare.Digits.Length)));
            return 1 + bare.Length;
        }

        // "a tenth", "a third of a second", "a second and a half", "a half second".
        if (w is "a" or "an")
        {
            // WHY (23 Nov 2026, bug hunt 24): every "a tenth" was read as
            // 0.1 of a second, so "a tenth-place finish for Dee" or "a tenth
            // win here for Sam" carried a 0.1 the facts never hold, and a
            // true sentence was cut from the recap. Here the tenth is an
            // ordinal, which is never a figure - stepped over like "his
            // tenth win" already is.
            if (OrdinalAfterA(ws, i))
            {
                return 2;
            }

            if (FractionAt(ws, i + 1) is { } unit)
            {
                found.Add(new Figure(Said(ws, i, 2), unit, false, unit));
                return 2;
            }

            if (PartOfASecond(ws, i + 1) is { } part)
            {
                found.Add(new Figure(Said(ws, i, 1 + part.Length), part.Value, false, 0.01));
                return 1 + part.Length;
            }

            if (Is(ws, i + 1, "second", "minute") && Is(ws, i + 2, "and") && Is(ws, i + 3, "a") && Is(ws, i + 4, "half"))
            {
                found.Add(new Figure(Said(ws, i, 5), 1.5, false, 0.5));
                return 5;
            }

            if (Is(ws, i + 1, "half") && Is(ws, i + 2, "second", "sec"))
            {
                found.Add(new Figure(Said(ws, i, 3), 0.5, false, 0.1));
                return 3;
            }

            return 1;
        }

        // "half a second".
        if (w == "half" && Is(ws, i + 1, "a") && Is(ws, i + 2, "second", "sec"))
        {
            found.Add(new Figure(Said(ws, i, 3), 0.5, false, 0.1));
            return 3;
        }

        if (Cardinal(ws, i) is not { } c)
        {
            return 1;
        }

        if (c.Ordinal)
        {
            return c.Length;
        }

        var after = i + c.Length;

        // "four point nine". Not "by one point five rounds from the end":
        // one point, and a count of something nobody has a decimal of.
        if (Is(ws, after, "point") && DigitWords(ws, after + 1) is { } digits
            && !(c.Value == 1 && c.Length == 1 && Is(ws, after + 1 + digits.Length, Counted)))
        {
            var len = c.Length + 1 + digits.Length;
            found.Add(new Figure(Said(ws, i, len), Parse($"{c.Value}.{digits.Digits}"), false, Math.Pow(10, -digits.Digits.Length)));
            return len;
        }

        // "seven-tenths", "four hundredths". Nobody says twelve tenths, and
        // "ten-tenths" is flat out, not a second.
        if (FractionAt(ws, after) is { } fraction)
        {
            if (c.Value > 0 && (fraction < 0.1 || c.Value < 10))
            {
                found.Add(new Figure(Said(ws, i, c.Length + 1), c.Value * fraction, false, fraction));
            }

            return c.Length + 1;
        }

        // "two thirds of a second", "three quarters of a second".
        if (c.Value is >= 1 and <= 3 && PartOfASecond(ws, after, c.Value) is { } share)
        {
            found.Add(new Figure(Said(ws, i, c.Length + share.Length), share.Value, false, 0.01));
            return c.Length + share.Length;
        }

        // "two and a half seconds".
        if (Is(ws, after, "and") && Is(ws, after + 1, "a") && Is(ws, after + 2, "half"))
        {
            found.Add(new Figure(Said(ws, i, c.Length + 3), c.Value + 0.5, false, 0.5));
            return c.Length + 3;
        }

        if (LapAt(ws, i, c) is { } lap)
        {
            found.Add(lap.Figure with { LapShaped = true });
            return lap.Length;
        }

        // A plain number. Nought, one and two in words are never a claim.
        if (forFacts || c.Value > 2 || c.Length > 1)
        {
            found.Add(new Figure(Said(ws, i, c.Length), c.Value, true, 1));
        }

        return c.Length;
    }

    private static int ReadDigits(List<Word> ws, int i, bool forFacts, List<Figure> found)
    {
        var w = ws[i].Low;
        var m = DigitFigure.Match(w);

        if (!m.Success)
        {
            if (Position.Match(w) is { Success: true } p)
            {
                found.Add(new Figure(ws[i].Raw, Parse(p.Groups["n"].Value), true, 1));
                return 1;
            }

            if (!forFacts)
            {
                // An ordinal, a clock, a car's model: not a claim this can read.
                return 1;
            }

            // The facts give up everything they have, so a clock backs its
            // minutes, its seconds and both as one ("1:30" backs ninety
            // seconds and a minute and a half).
            if (Clock.Match(w) is { Success: true } clock)
            {
                var minutes = Parse(clock.Groups["m"].Value);
                var seconds = Parse(clock.Groups["s"].Value);

                // OTHER, whatever is near it (8 Nov 2026): time on a clock
                // is neither a lap time nor a count of laps.
                found.Add(new Figure(w, minutes, true, 1) { Kind = FigureKind.Other });
                found.Add(new Figure(w, seconds, true, 1) { Kind = FigureKind.Other });
                found.Add(new Figure(w, (minutes * 60) + seconds, true, 1) { Kind = FigureKind.Other });
                found.Add(new Figure(w, minutes + (seconds / 60), false, 0.01) { Kind = FigureKind.Other });
                return 1;
            }

            foreach (Match number in AnyDigits.Matches(w))
            {
                var value = Parse(number.Value);
                found.Add(new Figure(w, value, !number.Value.Contains('.'), 1));
            }

            return 1;
        }

        var whole = m.Groups["int"].Success ? m.Groups["int"].Value : "0";
        var frac = m.Groups["frac"].Success ? m.Groups["frac"].Value : string.Empty;
        var unit = m.Groups["unit"].Value;
        var figure = Parse(frac.Length > 0 ? $"{whole}.{frac}" : whole);
        var isWhole = frac.Length == 0;
        var step = isWhole ? 1 : Math.Pow(10, -frac.Length);

        // Milliseconds in the facts back the seconds they make too.
        if (unit == "ms" || Is(ws, i + 1, "ms", "milliseconds", "millisecond"))
        {
            found.Add(new Figure(ws[i].Raw, figure, isWhole, step));
            found.Add(new Figure(ws[i].Raw, figure / 1000, false, step / 1000));
            return unit == "ms" ? 1 : 2;
        }

        // "3 tenths", "2 and a half".
        if (isWhole && FractionAt(ws, i + 1) is { } fraction)
        {
            found.Add(new Figure(Said(ws, i, 2), figure * fraction, false, fraction));
            return 2;
        }

        if (Is(ws, i + 1, "and") && Is(ws, i + 2, "a") && Is(ws, i + 3, "half"))
        {
            found.Add(new Figure(Said(ws, i, 4), figure + 0.5, false, 0.5));
            return 4;
        }

        // A year, said as one: not a claim about the race.
        if (!forFacts && isWhole && unit.Length == 0 && whole.Length == 4 && figure is >= 1900 and <= 2099)
        {
            return 1;
        }

        found.Add(new Figure(ws[i].Raw, figure, isWhole, step));

        // A class or a motor with no unit on it - "17.5 Stock", "6.5T" - is
        // said "seventeen five" as often as "seventeen point five", so the
        // facts back its two halves too. Never a time: a time has its "s".
        if (forFacts && frac.Length == 1 && unit.Length == 0)
        {
            found.Add(new Figure(ws[i].Raw, Parse(whole), true, 1) { Kind = FigureKind.Part });
            found.Add(new Figure(ws[i].Raw, Parse(frac), true, 1) { Kind = FigureKind.Part });
        }

        return 1;
    }

    /// <summary>
    /// A lap time said the way RaceNumbers.LapSpoken says one, starting with
    /// the whole seconds already read as <paramref name="c"/>.
    /// </summary>
    private static (Figure Figure, int Length)? LapAt(List<Word> ws, int i, (int Value, int Length, bool Ordinal) c)
    {
        if (c.Value is < 1 or > 59 || c.Length > 2)
        {
            return null;
        }

        var j = i + c.Length;
        var whole = new Figure(Said(ws, i, c.Length), c.Value, true, 1);

        if (j >= ws.Count)
        {
            return null;
        }

        // "five flat".
        if (ws[j].Low == "flat")
        {
            return (new Figure(Said(ws, i, c.Length + 1), c.Value, false, 0.001), c.Length + 1);
        }

        if (DigitWord(ws[j].Low) is { } tenth)
        {
            var k = j + 1;

            // "five four twenty-five" - the tenth, then the last two as a pair.
            if (Cardinal(ws, k) is { Ordinal: false } pair && pair.Value is >= 10 and <= 99)
            {
                var len = c.Length + 1 + pair.Length;

                return (new Figure(Said(ws, i, len), c.Value + (tenth / 10.0) + (pair.Value / 1000.0), false, 0.001)
                {
                    Apart = new[] { whole, new Figure(ws[j].Raw, tenth, true, 1), new Figure(Said(ws, k, pair.Length), pair.Value, true, 1) },
                    Joined = ws[j].Hyphened,
                }, len);
            }

            // "five oh nine six", "five one six nine" - three digits after
            // the seconds, the way a model writes a thousandth (8 Nov 2026):
            // "five-oh-nine-six" for 5.096 was read as 5.09 and a stray
            // "six", and seven true record-lap lines were thrown away.
            if (k + 1 < ws.Count && DigitWord(ws[k].Low) is { } hundredth3 && DigitWord(ws[k + 1].Low) is { } thousandth)
            {
                var len = c.Length + 3;

                return (new Figure(Said(ws, i, len), c.Value + (tenth / 10.0) + (hundredth3 / 100.0) + (thousandth / 1000.0), false, 0.001)
                {
                    Apart = new[] { whole, new Figure(ws[j].Raw, tenth, true, 1), new Figure(ws[k].Raw, hundredth3, true, 1), new Figure(ws[k + 1].Raw, thousandth, true, 1) },
                    Joined = tenth == 0 || ws[j].Hyphened,
                }, len);
            }

            // "five oh oh six", "six nine oh five".
            if (Is(ws, k, "oh") && k + 1 < ws.Count && DigitWord(ws[k + 1].Low) is { } last and > 0)
            {
                var len = c.Length + 3;

                return (new Figure(Said(ws, i, len), c.Value + (tenth / 10.0) + (last / 1000.0), false, 0.001)
                {
                    Apart = new[] { whole, new Figure(ws[j].Raw, tenth, true, 1), new Figure(ws[k + 1].Raw, last, true, 1) },
                    Joined = true,
                }, len);
            }

            // "six oh nine".
            if (tenth == 0 && k < ws.Count && DigitWord(ws[k].Low) is { } hundredth and > 0)
            {
                var len = c.Length + 2;

                return (new Figure(Said(ws, i, len), c.Value + (hundredth / 100.0), false, 0.01)
                {
                    Apart = new[] { whole, new Figure(ws[k].Raw, hundredth, true, 1) },
                    Joined = true,
                }, len);
            }

            return null;
        }

        // "five forty-two", "twelve thirty-four".
        if (Cardinal(ws, j) is { Ordinal: false } hundredths && hundredths.Value is >= 10 and <= 99 && hundredths.Length <= 2)
        {
            var len = c.Length + hundredths.Length;

            // "five sixteen nine", "five twenty-five eight" - the hundredths
            // as a pair, then the thousandth (8 Nov 2026): a model's 5.169
            // and 5.258, read before as 5.16 and 5.25 and a stray "nine".
            if (j + hundredths.Length < ws.Count && DigitWord(ws[j + hundredths.Length].Low) is { } thousandth)
            {
                return (new Figure(Said(ws, i, len + 1), c.Value + (hundredths.Value / 100.0) + (thousandth / 1000.0), false, 0.001)
                {
                    Apart = new[] { whole, new Figure(Said(ws, j, hundredths.Length), hundredths.Value, true, 1), new Figure(ws[j + hundredths.Length].Raw, thousandth, true, 1) },
                    Joined = ws[j].Hyphened,
                }, len + 1);
            }

            return (new Figure(Said(ws, i, len), c.Value + (hundredths.Value / 100.0), false, 0.01)
            {
                Apart = new[] { whole, new Figure(Said(ws, j, hundredths.Length), hundredths.Value, true, 1) },
                MaybeYear = c.Value is 19 or 20,
                Joined = ws[j].Hyphened,
            }, len);
        }

        return null;
    }

    /// <summary>A whole number in words, nought to nine hundred and ninety-nine - or an ordinal, flagged, so it can be stepped over.</summary>
    private static (int Value, int Length, bool Ordinal)? Cardinal(List<Word> ws, int i)
    {
        if (i >= ws.Count)
        {
            return null;
        }

        var w = ws[i].Low;

        if (Ordinals.Contains(w))
        {
            return (0, 1, true);
        }

        if (TensOf(w) is { } tens)
        {
            if (i + 1 < ws.Count)
            {
                var next = ws[i + 1].Low;

                if (SmallOf(next) is { } unit and >= 1 and <= 9)
                {
                    return (tens + unit, 2, false);
                }

                // "twenty-first" is a place, not a number.
                if (ws[i + 1].Hyphened && Ordinals.Contains(next))
                {
                    return (0, 2, true);
                }
            }

            return (tens, 1, false);
        }

        if (SmallOf(w) is not { } small)
        {
            return null;
        }

        if (small is >= 1 and <= 9 && Is(ws, i + 1, "hundred"))
        {
            var value = small * 100;
            var length = 2;
            var k = i + 2;

            if (Is(ws, k, "and"))
            {
                k++;
            }

            // "one hundred and fifty" - and not "one hundred five hundred".
            if (Cardinal(ws, k) is { Ordinal: false } rest && rest.Value is > 0 and < 100 && !Is(ws, k + rest.Length, "hundred"))
            {
                value += rest.Value;
                length = k - i + rest.Length;
            }

            return (value, length, false);
        }

        return (small, 1, false);
    }

    /// <summary>"tenth(s)" as 0.1, "hundredth(s)" as 0.01, "thousandth(s)" as 0.001.</summary>
    private static double? FractionAt(List<Word> ws, int i) =>
        i >= ws.Count
            ? null
            : ws[i].Low switch
            {
                "tenth" or "tenths" => 0.1,
                "hundredth" or "hundredths" => 0.01,
                "thousandth" or "thousandths" => 0.001,
                _ => null,
            };

    /// <summary>
    /// Whether the "tenth", "hundredth" or "thousandth" after the "a" at
    /// <paramref name="i"/> is a place in a count ("a tenth-place finish",
    /// "a tenth win", "a hundredth start") rather than a part of a second
    /// ("a tenth", "a tenth clear", "a tenth a lap", "a tenth-of-a-second
    /// gap"): it is when the next word is joined to it by a hyphen (other
    /// than "of") or is one of the things a club counts (bug hunt 24).
    /// </summary>
    private static bool OrdinalAfterA(List<Word> ws, int i)
    {
        if (!Is(ws, i + 1, "tenth", "hundredth", "thousandth") || i + 2 >= ws.Count)
        {
            return false;
        }

        var next = ws[i + 2];
        return (next.Hyphened && next.Low != "of") || OrdinalNouns.Contains(next.Low);
    }

    private static readonly HashSet<string> OrdinalNouns = new(StringComparer.Ordinal)
    {
        "place", "placed", "win", "victory", "start", "podium", "straight", "consecutive", "race", "heat",
        "main", "round", "title", "season", "year", "finish", "night", "appearance", "career",
    };

    /// <summary>"third of a second" as a third, "quarters of a second" as quarters - times <paramref name="many"/>.</summary>
    private static (double Value, int Length)? PartOfASecond(List<Word> ws, int i, int many = 1)
    {
        if (i >= ws.Count || !(Is(ws, i + 1, "of") && Is(ws, i + 2, "a") && Is(ws, i + 3, "second", "sec")))
        {
            return null;
        }

        return ws[i].Low switch
        {
            "third" or "thirds" => (many / 3.0, 4),
            "quarter" or "quarters" => (many / 4.0, 4),
            _ => null,
        };
    }

    /// <summary>"three oh four" after a "point" - the digits, and how many words.</summary>
    private static (string Digits, int Length)? DigitWords(List<Word> ws, int i)
    {
        var digits = new StringBuilder();
        var k = i;

        while (k < ws.Count && DigitWord(ws[k].Low) is { } d)
        {
            digits.Append(d);
            k++;
        }

        // ENDING IN A PAIR (11 Nov 2026): "point one seventy-nine" is .179,
        // "point seventeen" .17 - said the way "a five four twenty-five" is.
        // Read as "point one" and a stray seventy-nine, a qualifying time
        // the facts had exactly was cut as invented, and with it the
        // pole-sitter's whole introduction. Only up to thousandths.
        if (digits.Length <= 1 && k < ws.Count)
        {
            var w = ws[k].Low;

            if (SmallOf(w) is { } teen and >= 10)
            {
                return (digits.Append(teen).ToString(), k + 1 - i);
            }

            if (TensOf(w) is { } tens)
            {
                var unit = k + 1 < ws.Count && ws[k + 1].Hyphened && SmallOf(ws[k + 1].Low) is { } u and >= 1 and <= 9 ? u : 0;
                var used = unit > 0 ? 2 : 1;
                return (digits.Append(tens + unit).ToString(), k + used - i);
            }
        }

        return digits.Length == 0 ? null : (digits.ToString(), k - i);
    }

    private static int? DigitWord(string w) =>
        w is "oh" or "nought" or "zero" ? 0 : SmallOf(w) is { } s and <= 9 ? s : null;

    private static readonly string[] Small =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    };

    private static readonly string[] Tens =
    {
        string.Empty, string.Empty, "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
    };

    private static readonly HashSet<string> Ordinals = new(StringComparer.Ordinal)
    {
        "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth",
        "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth",
        "nineteenth", "twentieth", "thirtieth", "fortieth", "fiftieth", "sixtieth", "seventieth", "eightieth",
        "ninetieth", "hundredth",
    };

    private static int? SmallOf(string w)
    {
        if (w == "nought")
        {
            return 0;
        }

        var at = Array.IndexOf(Small, w);
        return at >= 0 ? at : null;
    }

    private static int? TensOf(string w)
    {
        var at = Array.IndexOf(Tens, w);
        return at >= 2 ? at * 10 : null;
    }

    // THE NOUN "POINT" (bug hunt 17): "at this point three drivers can
    // still win it" was read as 0.3 and "one point five rounds from the
    // end" as 1.5, and a correct sentence was cut as an invented figure.
    // A word in front that only a noun takes, or a count of whole things
    // after the number, says the point is not a decimal point.
    private static readonly string[] PointAsNoun =
    [
        "this", "that", "which", "what", "some", "any", "every", "each", "no", "single",
        "turning", "halfway", "starting", "breaking", "bonus", "championship",
    ];

    private static readonly string[] Counted =
    [
        "drivers", "cars", "racers", "rounds", "races", "heats", "mains", "finals", "laps", "nights", "meetings",
        "corners", "turns", "places", "positions", "spots", "wins", "podiums", "points",
    ];

    private static bool Is(List<Word> ws, int i, params string[] any) =>
        i >= 0 && i < ws.Count && any.Contains(ws[i].Low);

    /// <summary>The words as the line had them, hyphens and all.</summary>
    private static string Said(List<Word> ws, int i, int length)
    {
        var said = new StringBuilder();

        for (var k = i; k < i + length && k < ws.Count; k++)
        {
            if (k > i)
            {
                said.Append(ws[k].Hyphened ? '-' : ' ');
            }

            said.Append(ws[k].Raw);
        }

        return said.ToString();
    }

    private static double Parse(string digits) =>
        double.Parse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}

/// <summary>
/// Where the number check tells its caller that it dropped a line, and over
/// which figure (8 Nov 2026) - handed in the way SponsorCreditsHeld is, so
/// the director can account for a line that WAS written (the relay charged
/// it) and say why it was not heard. See CommentaryNumberCheck.
/// </summary>
public sealed class FiguresChecked
{
    /// <summary>The figure the facts could not back, as the line said it; null while nothing has been dropped.</summary>
    public string? Refused { get; internal set; }

    /// <summary>
    /// The line it was in, as the model wrote it (8 Nov 2026): a race recap
    /// was dropped for "six" and nobody could say what it had said, so the
    /// figure alone was no help in telling a wrong line from a good one.
    /// </summary>
    public string? Line { get; internal set; }
}
