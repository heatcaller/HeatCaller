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
    /// The beat the booth leaves between one voice and the other.
    ///
    /// WHY AN EXCHANGE NEEDED THIS AT ALL
    ///
    /// The turns were already split and already said in two voices, and it
    /// still came out sounding concatenated - because they were queued back
    /// to back, and the announcer's queue speaks what it is given with no
    /// gap at all. Two people in a booth do not do that. The one who is
    /// about to answer waits for the other to finish the thought, and that
    /// wait is what tells a room there are two of them rather than one voice
    /// with a setting changed halfway through.
    ///
    /// Just under half a second. A real hand-over is quicker than people
    /// think - a full second reads as one of them having missed their cue -
    /// and this is time on air, so it is counted into what an item costs
    /// (ShowItem.Estimate) rather than quietly spent.
    ///
    /// It only happens where the staging can hold a gap open, which means
    /// inside a show block: see ShowTreatment, which waits for each turn to
    /// have been heard before it queues the next one. Said plainly, with no
    /// block around it, the turns still go out back to back as they always
    /// did - nothing is made worse by this, and the pacing arrives with the
    /// programme it belongs to.
    /// </summary>
    public static readonly TimeSpan HandoffBeat = TimeSpan.FromMilliseconds(450);

    /// <summary>
    /// How far the bed is nudged up across the hand-over, as a multiple of
    /// its own level.
    ///
    /// A hair over one. It is NOT the lift at the end of a read (ShowRun
    /// .LiftFactor, about 4 dB) and must not sound like one: that move means
    /// "the talking has stopped", and a room told that in the middle of a
    /// two-hander hears the segment end and then start again. This is the
    /// bed coming out from under the voice for the length of the beat and
    /// going straight back, which is barely a sound on its own and is the
    /// difference between a gap and a hole.
    ///
    /// Clamped where it is used, so a club running the bed near full scale
    /// cannot have a hand-over push it past the show's own ceiling.
    /// </summary>
    public const float HandoffNudge = 1.15f;

    /// <summary>
    /// Which turns the voice changes hands in FRONT of - the positions a
    /// beat goes into.
    ///
    /// Worked out here rather than at each call site because "the voice
    /// changed" is this file's business: the tags, the split and who is
    /// talking all live here, and a caller comparing FromCoHost for itself
    /// is a caller that will get it wrong the day a three-way exchange
    /// exists. Never includes 0 - there is nothing in front of the first
    /// turn to hand over from.
    /// </summary>
    public static IReadOnlyList<int> Handoffs(IReadOnlyList<ExchangeTurn> turns)
    {
        var at = new List<int>();

        for (var i = 1; i < turns.Count; i++)
        {
            if (turns[i].FromCoHost != turns[i - 1].FromCoHost)
            {
                at.Add(i);
            }
        }

        return at;
    }

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
