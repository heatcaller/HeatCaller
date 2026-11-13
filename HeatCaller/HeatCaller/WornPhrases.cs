namespace HeatCaller;

/// <summary>
/// The stock phrases the model leans on, and which of them it has
/// already used tonight (16 Oct 2026).
///
/// From the floor on 16 Oct: every recap called the win a "masterclass",
/// and the openings were all rivals "locking horns" in a "showdown".
/// One recap on its own reads fine; the same word after every race is
/// what makes the room notice it is a machine. So:
///
/// - a few words are never wanted at all (Never) - "masterclass" is the
///   one that was heard after every race;
/// - the rest are fine ONCE, and are fed back to the model as "already
///   said tonight" from the moment it has used one (Watched), so the next
///   line has to find another way to say it.
///
/// Every line the model writes is noted (CommentaryService.RecordAndReturn)
/// and every system prompt carries the result (CommentaryService
/// .BuildSystemPrompt). Held for the run of the app - a night - and capped,
/// so the instruction never grows into a paragraph of its own.
/// </summary>
internal static class WornPhrases
{
    /// <summary>Never wanted, whatever the night has said so far.</summary>
    internal static readonly string[] Never =
    {
        "masterclass", "master class", "clinic", "ladies and gentlemen", "folks",
    };

    /// <summary>
    /// Fine once a night. Matched as written, ignoring case, so "dominat"
    /// catches dominated, dominant and domination alike - the word the
    /// prompt is then told to leave alone is the first form heard.
    /// </summary>
    internal static readonly string[] Watched =
    {
        "dominat", "in another league", "another gear", "league of", "untouchable", "on fire",
        "electric", "buckle up", "battle royale", "throw down", "lock horns", "locking horns",
        "at each other's throats", "champions are crowned", "legacies", "showdown", "spectacular",
        "incredible", "unbelievable", "thrilling", "fierce", "sheer", "historic", "commanding",
        "supremacy", "edge of your seat", "nail-biter", "running away with it", "precision",
        "composure", "masterful", "flawless", "statement", "in the groove", "keep that rhythm",
        "out for blood", "tension", "moments away", "this is it", "you can feel", "jaw-dropping",
        "blistering", "the stuff of legend", "pure class",
    };

    /// <summary>The most "already said" phrases the prompt carries at once - the latest ones.</summary>
    private const int MostListed = 16;

    private static readonly object Gate = new();

    /// <summary>The watched phrases heard tonight, oldest first, as they were actually written.</summary>
    private static readonly List<string> Heard = new();

    /// <summary>Notes the stock phrases a line the model wrote has used.</summary>
    internal static void Note(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (Gate)
        {
            foreach (var phrase in Watched)
            {
                var at = line.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);

                // Only from the start of a word. "attention" is not "tension",
                // nor "prehistoric" "historic" - the phrase may run on into
                // the rest of a word (see Watched) but never begin inside one.
                while (at > 0 && char.IsLetter(line[at - 1]))
                {
                    at = line.IndexOf(phrase, at + 1, StringComparison.OrdinalIgnoreCase);
                }

                if (at < 0)
                {
                    continue;
                }

                // The word as heard - "dominated" rather than "dominat".
                var end = at + phrase.Length;

                while (end < line.Length && char.IsLetter(line[end]))
                {
                    end++;
                }

                var heard = line[at..end].ToLowerInvariant();

                Heard.RemoveAll(h => string.Equals(h, heard, StringComparison.OrdinalIgnoreCase));
                Heard.Add(heard);
            }

            if (Heard.Count > MostListed * 2)
            {
                Heard.RemoveRange(0, Heard.Count - MostListed * 2);
            }
        }
    }

    /// <summary>Forgets the night's phrases - for tests, and a fresh night.</summary>
    internal static void Forget()
    {
        lock (Gate)
        {
            Heard.Clear();
        }
    }

    /// <summary>
    /// The instruction every system prompt ends with: the words never to
    /// use, and the ones already used tonight. Starts with a space.
    /// </summary>
    internal static string PromptLine()
    {
        string[] said;

        lock (Gate)
        {
            said = Heard.Skip(Math.Max(0, Heard.Count - MostListed)).ToArray();
        }

        var line = " Never use these words: " + string.Join(", ", Never.Select(w => $"\"{w}\"")) + ".";

        if (said.Length > 0)
        {
            line += " These have already been said tonight, so find fresh words instead of: "
                    + string.Join(", ", said.Select(w => $"\"{w}\"")) + ".";
        }

        return line + " Vary your phrasing - never reuse a stock line.";
    }
}
