namespace HeatCaller;

/// <summary>One person's turn in a two-voice segment.</summary>
/// <param name="FromCoHost">True when the co-host says it, false when the announcer does.</param>
/// <param name="Line">What they say, with the name and the colon already taken off.</param>
public sealed record ExchangeTurn(bool FromCoHost, string Line);

/// <summary>
/// Turns one written segment into a conversation between the announcer
/// and a second voice.
///
/// WHY A SECOND VOICE IS WORTH THE TROUBLE
///
/// The mechanic's interview already proved it: two voices stop sounding
/// like an app reading out facts and start sounding like a broadcast,
/// and the effect is out of all proportion to the work. One voice saying
/// "Sam has been within four tenths of Bill for six laps" is information.
/// The same fact as a question and an answer is coverage.
///
/// ONE REQUEST, NOT TWO
///
/// The whole exchange is written in a single call and split here. Two
/// calls would cost twice the allowance, take twice as long - and, worse,
/// the second half would be written without having seen the first, so the
/// answer would not actually answer the question. The same reason the
/// mechanic's interview has always worked this way.
///
/// IF IT CANNOT BE SPLIT, IT IS NOT USED
///
/// A model that ignores the format and writes a paragraph gets its line
/// dropped rather than read out by whichever voice happened to be next.
/// The caller falls back to the single-voice version of the same segment,
/// which costs nothing extra because the facts are already in hand.
/// </summary>
public static class CommentaryExchange
{
    /// <summary>What the announcer's turns are labelled with in the model's answer.</summary>
    public const string AnnouncerTag = "A:";

    /// <summary>And the co-host's.</summary>
    public const string CoHostTag = "B:";

    /// <summary>
    /// Nobody wants a six-turn conversation about a lap time. Two or four
    /// turns is a remark and a reply; past that it stops being a segment
    /// and starts being a podcast over the top of a race.
    /// </summary>
    public const int MostTurns = 4;

    /// <summary>
    /// How the model is told to write an exchange.
    ///
    /// The tags are deliberately "A" and "B" rather than the real names: a
    /// co-host called "Dave" invites the model to write "Dave:" in the
    /// middle of a sentence as well as at the start of a line, and then
    /// the split puts half a sentence in the wrong voice. Names go in as
    /// context, tags stay neutral.
    /// </summary>
    /// <param name="announcer">What the announcer is called, for colour. May be blank.</param>
    /// <param name="coHost">What the co-host is called. May be blank.</param>
    /// <param name="turns">How many turns to ask for. Clamped to MostTurns.</param>
    public static string Brief(string? announcer, string? coHost, int turns = 2)
    {
        var wanted = Math.Clamp(turns, 2, MostTurns);

        var who = string.Empty;
        var a = CommentaryService.Field(announcer);
        var b = CommentaryService.Field(coHost);

        if (a.Length > 0 && b.Length > 0)
        {
            who = $" A is {a}, the lead commentator; B is {b}, alongside them.";
        }
        else if (b.Length > 0)
        {
            who = $" B is {b}, alongside the lead commentator.";
        }

        return $"Write this as a short exchange between two commentators in the booth.{who} "
             + $"Use exactly {wanted} lines, each starting with \"{AnnouncerTag}\" or \"{CoHostTag}\", alternating and starting with {AnnouncerTag}. "
             + "Put nothing else in the answer - no stage directions, no blank lines, no names of your own. "
             + "Both of them stick to the facts above; neither invents a result, a lap time or a prediction.";
    }

    /// <summary>
    /// Splits what came back into turns, or returns nothing when it did
    /// not come back in the right shape.
    ///
    /// Deliberately strict about the shape and forgiving about the
    /// spacing. A model will put a stray blank line in, or lowercase the
    /// tag, or bold it; none of those change who is talking. A model that
    /// writes a paragraph with no tags at all has ignored the brief, and
    /// guessing which half belongs to whom would put words in the wrong
    /// voice over a PA.
    /// </summary>
    public static IReadOnlyList<ExchangeTurn> Split(string? written)
    {
        var turns = new List<ExchangeTurn>();

        if (string.IsNullOrWhiteSpace(written))
        {
            return turns;
        }

        foreach (var raw in written.Split('\n'))
        {
            // Asterisks and hashes: a model asked for "A:" quite often
            // offers "**A:**" instead, and that is the same line.
            var line = raw.Trim().Trim('*', '#', '-', ' ', '\t');

            if (line.Length == 0)
            {
                continue;
            }

            bool fromCoHost;

            if (StartsWithTag(line, AnnouncerTag))
            {
                fromCoHost = false;
            }
            else if (StartsWithTag(line, CoHostTag))
            {
                fromCoHost = true;
            }
            else
            {
                // An untagged line in the middle of a tagged answer is the
                // model carrying on past its brief. The exchange is
                // whatever was properly tagged up to that point.
                continue;
            }

            var said = line[2..].Trim().Trim('*', ' ');

            if (said.Length == 0)
            {
                continue;
            }

            turns.Add(new ExchangeTurn(fromCoHost, said));

            if (turns.Count == MostTurns)
            {
                break;
            }
        }

        // One voice talking is not an exchange - it is the single-voice
        // segment with a label on it, and the caller is better off with
        // the real one.
        if (turns.Count < 2 || turns.All(t => t.FromCoHost == turns[0].FromCoHost))
        {
            return Array.Empty<ExchangeTurn>();
        }

        return turns;
    }

    /// <summary>
    /// The whole exchange as one piece of text, for the Announcer screen
    /// and for "repeat that" - which show what was said rather than
    /// replaying it in two voices.
    /// </summary>
    public static string Flatten(IReadOnlyList<ExchangeTurn> turns, string? announcer, string? coHost)
    {
        var a = CommentaryService.Field(announcer);
        var b = CommentaryService.Field(coHost);

        return string.Join("\n", turns.Select(t =>
        {
            var who = t.FromCoHost ? b : a;

            return who.Length > 0 ? $"{who}: {t.Line}" : t.Line;
        }));
    }

    private static bool StartsWithTag(string line, string tag) =>
        line.Length > tag.Length && line.StartsWith(tag, StringComparison.OrdinalIgnoreCase);
}
