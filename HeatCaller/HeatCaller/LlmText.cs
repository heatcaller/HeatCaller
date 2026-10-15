namespace HeatCaller;

/// <summary>
/// Tidying a model's answer into something safe to speak.
///
/// Shared by both local brains, and that is the whole reason it exists as
/// its own class. Every rule in here was put there by a race night going
/// wrong: the label strip, the sentence truncation, the tagged two-hander
/// that must keep its line breaks. A second brain that wrote its own
/// version of this would re-introduce every one of those bugs on its first
/// evening, so there is one copy and both clients call it.
///
/// Lifted out of <see cref="LocalLlmClient"/> unchanged. The methods there
/// still exist and still have the same names, as thin forwarders, so the
/// tests that already cover this behaviour keep pointing at the class they
/// were written against.
/// </summary>
internal static class LlmText
{
    /// <summary>
    /// True when the brief asked for an answer tagged line by line - the
    /// co-host's two-hander, as CommentaryExchange.Brief asks for it.
    ///
    /// Read off the prompt because there is nowhere else to read it from:
    /// the brain is spoken to through ICommentaryBrain, and GenerateAsync
    /// says nothing about the shape of the answer wanted. The brief is the
    /// only thing this app sends that puts those two tags in quotes.
    /// </summary>
    internal static bool AsksForTaggedTurns(string? userPrompt) =>
        userPrompt is not null
        && userPrompt.Contains($"\"{CommentaryExchange.AnnouncerTag}\"", StringComparison.Ordinal)
        && userPrompt.Contains($"\"{CommentaryExchange.CoHostTag}\"", StringComparison.Ordinal);

    /// <summary>
    /// The text up to and including its last complete sentence, or null
    /// when there isn't one worth speaking.
    ///
    /// A sentence ends at . ! or ? (with any closing quote or bracket after
    /// it) followed by a space or the end. A full stop between two digits
    /// is a decimal - "4.63 seconds" - not an ending.
    /// </summary>
    internal static string? UpToLastWholeSentence(string text)
    {
        var trimmed = text.TrimEnd();

        for (var i = trimmed.Length - 1; i >= 0; i--)
        {
            var ch = trimmed[i];

            if (ch is not ('.' or '!' or '?'))
            {
                continue;
            }

            if (ch == '.' && i > 0 && i + 1 < trimmed.Length && char.IsDigit(trimmed[i - 1]) && char.IsDigit(trimmed[i + 1]))
            {
                continue;
            }

            var end = i + 1;

            while (end < trimmed.Length && trimmed[end] is '"' or '\'' or '”' or '’' or ')' or ']')
            {
                end++;
            }

            if (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
            {
                continue;
            }

            var kept = trimmed[..end].Trim();
            return kept.Length >= 20 ? kept : null;
        }

        return null;
    }

    /// <summary>
    /// Tidies a model response into something safe to speak.
    ///
    /// Small models routinely wrap output in quotes, prefix it with
    /// "Commentary:", or add a second sentence after being asked for one.
    /// Rather than fight that with prompt engineering alone, the worst of it
    /// is stripped here - and anything still too long is rejected outright,
    /// since an over-long line blocks the announcement queue behind it.
    /// </summary>
    /// <param name="taggedTurns">
    /// True when the answer is a tagged two-hander and must reach
    /// CommentaryExchange.Split with its tags and its line breaks intact.
    /// </param>
    internal static string? CleanLine(string? text, int maxTokens, bool taggedTurns = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Trim();

        // The label strip and the newline collapse below are both for a
        // single line headed straight for the speech queue, and both
        // destroy a two-hander: the first takes the "A:" off the opening
        // turn, the second runs the turns together, and Split then finds
        // nothing tagged. So the co-host never once worked on Ollama -
        // every exchange fell through to a second, single-voice write, and
        // BattleWatch's twenty seconds were gone by the time it did.
        // CommentaryService.ParseInterview has guarded the interview
        // against exactly this all along.
        if (!taggedTurns)
        {
            // Strip a leading label like "Commentary:" or "Announcer:".
            var colon = line.IndexOf(':');
            if (colon > 0 && colon < 16 && !line[..colon].Contains(' '))
            {
                line = line[(colon + 1)..].Trim();
            }
        }

        line = line.Trim('"', '“', '”', '\'').Trim();

        if (taggedTurns)
        {
            // The breaks stay, because Split looks for a tag at the start
            // of a line. Only the carriage returns go, so a model that
            // answers with CRLF still splits.
            line = line.Replace("\r\n", "\n").Replace('\r', '\n');
        }
        else
        {
            // Collapse newlines into spaces - this is headed for a speech
            // queue, so paragraph breaks have no meaning, but the text itself
            // is kept. Longer settings legitimately produce multi-sentence
            // answers now.
            line = line.Replace('\r', ' ').Replace('\n', ' ');
        }

        while (line.Contains("  "))
        {
            line = line.Replace("  ", " ");
        }

        // Scaled to what the caller actually asked for, with generous
        // headroom. This was a fixed 240 characters, which silently
        // REJECTED any longer answer and returned null - so raising the
        // word limit produced nothing at all rather than a longer line.
        var maxChars = Math.Max(240, maxTokens * 8);

        if (line.Length == 0 || line.Length > maxChars)
        {
            return null;
        }

        return line;
    }
}
