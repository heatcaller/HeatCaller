namespace HeatCaller;

/// <summary>
/// How much of a read fitted into the gap it was asked for, and where to
/// pick the copy up next time.
/// </summary>
/// <param name="Text">The sentences that fit, as written. Empty when none did.</param>
/// <param name="ResumeAt">Where in the copy the next read starts. Zero when the copy is finished.</param>
/// <param name="More">True when there is copy left that this gap had no room for.</param>
internal readonly record struct CopyFit(string Text, int ResumeAt, bool More);

/// <summary>
/// A sponsor's paid sales copy: what is kept of it, how it breaks into
/// sentences, and how much of it a gap has room for.
///
/// WHY THIS IS NOT THE TAGLINE
///
/// A tagline is a label - "Everything for the Mini-Z racer" - and it is
/// hung on the end of a sentence SponsorRotation wrote. Sales copy is the
/// other way round: somebody the club sold a slot to wrote a few sentences
/// to be spoken aloud, and the app's job is to say them as written and put
/// a name in front of them. Nothing in here rewords anything, for the
/// reason SponsorRotation gives at length: the one piece of text in this
/// app that must come out exactly as sold is the bit the sponsor paid for.
///
/// WHOLE SENTENCES, ALWAYS
///
/// A gap between races is not a fixed length and nobody knows it in
/// advance, so copy written for twenty seconds will sometimes be offered
/// eight. The answer is never to cut mid-sentence: "come down to Clarke and
/// Sons for all your" is worse for the sponsor than saying nothing at all,
/// and worse still than saying the first half tonight and the rest in the
/// next gap, which is what this does. See <see cref="Fit"/>.
///
/// WHY THERE IS A LENGTH AT ALL
///
/// Because it is read by a synthetic voice over a PA, in a hall, between
/// races. Kokoro reads at a steady pace and does not get bored, but a crowd
/// does: past about half a minute an advert stops being heard and starts
/// being waited out. <see cref="MaxChars"/> is the cap on what is stored
/// and <see cref="LongestRead"/> the cap on what goes out in one go,
/// whatever the gap - and the second is the one that matters, because a
/// read is queued as ONE utterance and a flag call can only go in front of
/// what has not been spoken yet, not into the middle of it.
/// </summary>
internal static class SponsorCopy
{
    /// <summary>
    /// The most sales copy a sponsor's record keeps, in characters.
    ///
    /// Six hundred is around a hundred words, which is about forty seconds
    /// read aloud - a long advert by any club's standards and more than
    /// <see cref="LongestRead"/> will send out at once, so a club that
    /// writes to the cap gets it over two gaps rather than losing the end
    /// of it. The Sponsors page holds the box to the same number, so what
    /// is typed is what is kept: a cap that silently eats the last sentence
    /// is how a club finds out about this from the sponsor.
    /// </summary>
    internal const int MaxChars = 600;

    /// <summary>
    /// How fast a sales read is reckoned to be spoken, in words a minute.
    ///
    /// Deliberately slower than a race call. A hundred and fifty is an
    /// unhurried read - it is what a human voiceover does with ad copy, and
    /// it is also the safe side of whatever speed the club has the voice
    /// set to: an estimate that runs under the real thing means a read
    /// spilling past its gap, and the gap is where the next race is.
    /// </summary>
    internal const int WordsPerMinute = 150;

    /// <summary>
    /// The shortest gap worth starting a read into. Under this nothing is
    /// read at all - a sponsor's name and one sentence need a few seconds,
    /// and a read begun in the last breath before a start is a read nobody
    /// hears the end of.
    /// </summary>
    internal static readonly TimeSpan ShortestUsefulGap = TimeSpan.FromSeconds(6);

    /// <summary>
    /// The most that goes out in one read, however much room there is.
    ///
    /// A read is handed to the announcer as a single line, and the voice
    /// queue can only put a flag call IN FRONT of what has not been spoken
    /// yet - it cannot cut into the middle of one utterance. So the way to
    /// keep a sales read from ever sitting across a race call is to keep
    /// any one of them short, and let the rest of the copy wait for the
    /// next gap. Twenty-five seconds is two or three sentences.
    /// </summary>
    internal static readonly TimeSpan LongestRead = TimeSpan.FromSeconds(25);

    /// <summary>
    /// "As much as <see cref="LongestRead"/> allows" - for the moments with
    /// no race anywhere near them, the top of the night and the sign-off.
    /// Not actually unlimited: see <see cref="LongestRead"/>.
    /// </summary>
    internal static readonly TimeSpan NoLimit = TimeSpan.MaxValue;

    /// <summary>Nothing fitted, and nothing to pick up.</summary>
    private static readonly CopyFit Nothing = new(string.Empty, 0, false);

    /// <summary>
    /// The copy as it will be spoken: one block, no brackets, no line
    /// breaks, no longer than <see cref="MaxChars"/>.
    ///
    /// Cleaned through CommentaryService.Field, which is the same guard
    /// every other piece of club-typed text goes through on its way to
    /// being said - and it matters more here than almost anywhere, because
    /// sales copy is the one field in the app that is written by somebody
    /// outside the club and pasted in by somebody inside it.
    ///
    /// Read a little longer than the cap on purpose, so the trim below has
    /// something to trim back FROM: Field's own cap is a hard chop at the
    /// character, which in spoken copy lands mid-word.
    /// </summary>
    internal static string Tidy(string? copy)
    {
        var cleaned = CommentaryService.Field(copy, MaxChars + 120);

        if (cleaned.Length <= MaxChars)
        {
            return cleaned;
        }

        // Back to the last sentence that finished inside the cap, and
        // failing that the last whole word. Half a sentence read out over a
        // PA sounds like the app has crashed.
        var head = cleaned[..MaxChars];
        var stop = head.LastIndexOfAny(new[] { '.', '!', '?' });

        if (stop > 0)
        {
            return head[..(stop + 1)];
        }

        var space = head.LastIndexOf(' ');

        return (space > 0 ? head[..space] : head).TrimEnd();
    }

    /// <summary>
    /// The copy broken into sentences.
    ///
    /// Every piece carries its own trailing space, so the pieces put back
    /// together are EXACTLY the string handed in. That is not tidiness: a
    /// read that runs over two gaps carries a character offset from one to
    /// the other (see <see cref="Fit"/>), and an offset into a string whose
    /// pieces do not add back up to it points at the wrong word.
    ///
    /// A full stop only ends a sentence when a space or the end of the copy
    /// follows it, so "9.99", "co.uk" and "No. 4" stay whole. An
    /// abbreviation followed by a space - "Ltd. " - is read as the end of a
    /// sentence, which costs a pause the copy did not ask for and nothing
    /// worse.
    /// </summary>
    internal static IReadOnlyList<string> Sentences(string copy)
    {
        var found = new List<string>();

        if (string.IsNullOrEmpty(copy))
        {
            return found;
        }

        var start = 0;

        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] is not ('.' or '!' or '?'))
            {
                continue;
            }

            if (copy[i] == '.' && !EndsALine(copy, i + 1))
            {
                continue;
            }

            // Run on through "..." and whatever closes the sentence, then
            // take the spaces after it as part of this piece - see above.
            var end = i + 1;

            while (end < copy.Length && copy[end] is '.' or '!' or '?' or '"' or '\'' or ')')
            {
                end++;
            }

            while (end < copy.Length && copy[end] == ' ')
            {
                end++;
            }

            found.Add(copy[start..end]);
            start = end;

            // end is always past i, so start only ever moves forwards.
            i = end - 1;
        }

        if (start < copy.Length)
        {
            found.Add(copy[start..]);
        }

        return found;
    }

    /// <summary>Whether what follows a full stop makes it the end of a sentence rather than part of a number or an address.</summary>
    private static bool EndsALine(string copy, int next) =>
        next >= copy.Length || copy[next] == ' ' || copy[next] is '"' or '\'' or ')';

    /// <summary>
    /// Roughly how long this is to say, at <see cref="WordsPerMinute"/>.
    ///
    /// Counted in words rather than characters because that is what a
    /// reading speed is quoted in, and because a long word and a short one
    /// take nearly the same time to say. An estimate is all this can be -
    /// the real length depends on the voice, the club's speed setting and
    /// the words themselves - so everything that uses it leaves room.
    /// </summary>
    internal static TimeSpan SpokenLength(string text)
    {
        var words = 0;
        var inWord = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWord = false;
                continue;
            }

            if (!inWord)
            {
                words++;
                inWord = true;
            }
        }

        return TimeSpan.FromSeconds(words * 60.0 / WordsPerMinute);
    }

    /// <summary>
    /// As much of the copy as <paramref name="gap"/> has room for, starting
    /// from <paramref name="from"/>, in whole sentences.
    ///
    /// THE ONE RULE THAT IS NOT ABOUT ROOM: the first sentence goes out
    /// whatever the arithmetic says, so long as the gap is at least
    /// <see cref="ShortestUsefulGap"/>. A sponsor whose first sentence is
    /// four seconds longer than tonight's gaps happen to be would otherwise
    /// never be read at all, and would never be told why - which is the
    /// worst thing this feature could do, since a read is something the
    /// club took money for. Everything after the first has to fit.
    ///
    /// Whatever is left comes back in <see cref="CopyFit.ResumeAt"/> and is
    /// read in the sponsor's next gap, with a lead-in in front of it (see
    /// SponsorRotation). The copy is therefore delivered whole over the
    /// evening rather than truncated in it.
    /// </summary>
    internal static CopyFit Fit(string copy, int from, TimeSpan gap)
    {
        if (string.IsNullOrEmpty(copy) || from < 0 || from >= copy.Length)
        {
            return Nothing;
        }

        // Never more than LongestRead in one go, however long the gap - see
        // there. Compared rather than Math.Min'd so that NoLimit, which is
        // TimeSpan.MaxValue, cannot overflow anything below.
        var room = gap < LongestRead ? gap : LongestRead;

        var at = from;
        var taken = 0;
        var spent = TimeSpan.Zero;

        foreach (var sentence in Sentences(copy[from..]))
        {
            var cost = SpokenLength(sentence);

            var fits = taken == 0
                ? gap >= ShortestUsefulGap
                : spent + cost <= room;

            if (!fits)
            {
                break;
            }

            spent += cost;
            taken += sentence.Length;
            at += sentence.Length;

            if (spent >= room)
            {
                break;
            }
        }

        if (taken == 0)
        {
            return Nothing;
        }

        // Trimmed for speaking; the offsets above were counted on the
        // untrimmed pieces, which is the whole reason Sentences keeps the
        // spaces with them.
        var text = copy.Substring(from, taken).Trim();
        var done = at >= copy.Length;

        return new CopyFit(text, done ? 0 : at, !done);
    }

    /// <summary>
    /// Whether <paramref name="at"/> is the end of one of this copy's
    /// sentences - asked before a half-read copy is picked up again.
    ///
    /// A club can edit the copy between one gap and the next, and an offset
    /// into last week's wording means nothing in this week's. Zero and
    /// anything past the end are both "start again", which is the only
    /// thing that can be read out loud without sounding like a fault.
    /// </summary>
    internal static bool IsBoundary(string copy, int at)
    {
        if (at <= 0 || at >= copy.Length)
        {
            return false;
        }

        var offset = 0;

        foreach (var sentence in Sentences(copy))
        {
            offset += sentence.Length;

            if (offset == at)
            {
                return true;
            }

            if (offset > at)
            {
                return false;
            }
        }

        return false;
    }
}
