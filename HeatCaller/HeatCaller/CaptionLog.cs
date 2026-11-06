namespace HeatCaller;

/// <summary>One line the announcer said, as a caption.</summary>
/// <param name="N">Its number, counting up from 1 for as long as the app runs, so a page can tell a new line from one it has shown.</param>
/// <param name="Text">What was said, on one line, at most <see cref="CaptionLog.PieceChars"/> characters.</param>
/// <param name="AtUtc">When it was heard.</param>
public sealed record CaptionLine(long N, string Text, DateTime AtUtc)
{
    /// <summary>
    /// The whole spoken line this piece is part of, single-spaced - the same
    /// on every piece of one line. Null for a line made by hand (the tests),
    /// which is then its own whole line: see <see cref="Said"/>.
    /// </summary>
    /// <remarks>
    /// 6 Nov 2026, Bug hunt 11: since 26 Oct a long line is captioned a piece
    /// at a time, and the public captions (MainForm.PublicCaptionLines)
    /// judged each piece on its own. Only the first piece of "Black flag,
    /// Sam Smith. &lt;typed reason&gt;. Please bring your car in..." starts
    /// "Black flag,", so the reason went onto the club's relay captions; a
    /// later piece of a line naming an initials-only driver, or a car by its
    /// transponder number, doesn't repeat the name, so it went out too. Every
    /// piece now carries the line it came from, and the public filters judge
    /// that once for all its pieces. Never written into any document: the
    /// captions JSON names its fields (n, t, at) one by one.
    /// </remarks>
    public string? WholeText { get; init; }

    /// <summary>What the public filters judge: the whole line this piece came from, or the piece itself when it has none.</summary>
    public string Said => WholeText ?? Text;

    /// <summary>
    /// Which piece of <see cref="WholeText"/> this is, counting from 0 - its
    /// place in <see cref="CaptionLog.Pieces"/> of the whole line. 0 for a
    /// line made by hand.
    /// </summary>
    /// <remarks>
    /// 6 Nov 2026, Bug hunt 11b review: "Shorten published names" shortened
    /// each piece on its own, and a piece split inside a name ("...Sam" /
    /// "Smith...") held neither half as the full name, so nothing was
    /// shortened and the surname went online. The public captions now shorten
    /// the whole line, cut it again by the same rule and publish the piece at
    /// this index (MainForm.PublicCaptionLines). Never written into any
    /// document, like WholeText.
    /// </remarks>
    public int PieceIndex { get; init; }
}

/// <summary>
/// The last few lines the announcer said, as text, for anybody who can't
/// hear the PA: the Big Screen's captions strip, the OBS overlay and the
/// club's live page (22 Oct 2026, driver experience, item 9).
/// </summary>
/// <remarks>
/// Filled from AnnouncementService.LineHeard, which is raised on whatever
/// thread the line plays from, and read from the UI thread, the web
/// server's threads and the relay publisher's - so everything here is under
/// one lock and every read is a copy. Holds the text exactly as heard: the
/// public copy is redacted where it is published (PublicPrivacy.Redact),
/// not here, because the room's own screens show the full names.
///
/// A LONG LINE IS CAPTIONED A PIECE AT A TIME (26 Oct 2026, asked for by
/// Asher: "Big screen doesn't reliably show live captions" and "when
/// watching the live captions through the relay they stopped updating").
/// LineHeard is raised once per line, when it starts, and a recap, a round
/// intro or the driver introductions is ONE line of a minute or more. It
/// used to be kept as its first 300 characters and a "...": the Big Screen
/// showed that for twenty seconds and then went blank while the voice
/// carried on, and the live page and the overlay sat on that one cut line
/// until the announcer stopped talking. Now the line is split at its
/// sentences into pieces the strip can show whole, and each piece is
/// released when the voice should be reaching it - its place in the line at
/// <see cref="CharsPerSecond"/>. The pieces still waiting are let go at once
/// when the line finishes (<see cref="SaidInFull"/>) or when the next line
/// starts, and thrown away when the line is cut off (<see cref="Unsaid"/>),
/// so a caption never shows words the room did not hear. Numbers are given
/// as a piece is released, so they still only ever count up.
/// </remarks>
internal sealed class CaptionLog
{
    /// <summary>Lines kept. Enough for any reader; a caption older than this has long scrolled away.</summary>
    public const int Kept = 12;

    /// <summary>
    /// The longest piece a long line is captioned in - two lines of the Big
    /// Screen's strip, so a piece is shown whole rather than cut with "...".
    /// </summary>
    public const int PieceChars = 140;

    /// <summary>
    /// The most pieces one line is captioned in - about ten minutes of
    /// talking. Anything after that ends the last piece with "...".
    /// </summary>
    public const int MostPieces = 64;

    /// <summary>
    /// Roughly how fast the announcer speaks, in characters a second - the
    /// Big Screen's own measure (BigScreenForm.CaptionCharsPerSecond), used
    /// to work out when the voice reaches each piece of a long line.
    /// </summary>
    public const double CharsPerSecond = 15;

    /// <summary>The one log the whole app shares.</summary>
    public static CaptionLog Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Queue<CaptionLine> _lines = new();

    /// <summary>The rest of the line being said: its pieces not reached yet, each with when it is due. Numbered as released.</summary>
    private readonly Queue<(string Text, string Whole, int Index, DateTime DueUtc)> _waiting = new();

    private long _next;

    /// <summary>
    /// Adds a line as heard now (or at <paramref name="atUtc"/>), and returns
    /// its first piece as kept. Null for a line with nothing in it, which is
    /// not kept. Whatever was still waiting of the line before is released
    /// first: a new line means the voice has finished the last one.
    /// </summary>
    public CaptionLine? Add(string? text, DateTime? atUtc = null)
    {
        var pieces = Pieces(text);

        if (pieces.Count == 0)
        {
            return null;
        }

        var at = atUtc ?? DateTime.UtcNow;

        lock (_gate)
        {
            ReleaseWaitingLocked(at);

            // 6 Nov 2026, Bug hunt 11: every piece carries the whole line, so
            // the public captions withhold all of a line or none of it - see
            // CaptionLine.WholeText.
            var whole = SingleSpaced(text);
            var first = KeepLocked(pieces[0], whole, 0, at);
            var said = pieces[0].Length + 1;

            for (var i = 1; i < pieces.Count; i++)
            {
                _waiting.Enqueue((pieces[i], whole, i, at + TimeSpan.FromSeconds(said / CharsPerSecond)));
                said += pieces[i].Length + 1;
            }

            return first;
        }
    }

    /// <summary>The last <paramref name="count"/> lines heard by now (at most <see cref="Kept"/>), oldest first.</summary>
    public IReadOnlyList<CaptionLine> Last(int count = Kept) => Last(count, DateTime.UtcNow);

    /// <summary>The same, as of <paramref name="nowUtc"/> - for the tests.</summary>
    internal IReadOnlyList<CaptionLine> Last(int count, DateTime nowUtc)
    {
        lock (_gate)
        {
            ReleaseDueLocked(nowUtc);

            return count <= 0
                ? Array.Empty<CaptionLine>()
                : _lines.Skip(Math.Max(0, _lines.Count - count)).ToList();
        }
    }

    /// <summary>The newest line's number, or 0 before anything has been said. Cheap: a page can ask every second.</summary>
    public long LatestN => LatestAt(DateTime.UtcNow);

    /// <summary>The same, as of <paramref name="nowUtc"/> - for the tests.</summary>
    internal long LatestAt(DateTime nowUtc)
    {
        lock (_gate)
        {
            ReleaseDueLocked(nowUtc);
            return _lines.Count == 0 ? 0 : _lines.Last().N;
        }
    }

    /// <summary>
    /// The line being said has finished: whatever of it was still waiting is
    /// shown now. Called when playback ends (AnnouncementService.PlayPcmAsync),
    /// because the voice can be quicker than <see cref="CharsPerSecond"/>.
    /// </summary>
    public void SaidInFull(DateTime? nowUtc = null)
    {
        lock (_gate)
        {
            ReleaseWaitingLocked(nowUtc ?? DateTime.UtcNow);
        }
    }

    /// <summary>
    /// The line being said was cut off or dropped before it finished - a
    /// flag, the countdown, the mute: what was still waiting of it is never
    /// shown, because the room never heard it.
    /// </summary>
    public void Unsaid()
    {
        lock (_gate)
        {
            _waiting.Clear();
        }
    }

    /// <summary>Forgets every line. The numbers carry on from where they were, so a reader never mistakes a new line for an old one.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _waiting.Clear();
        }
    }

    /// <summary>
    /// A line as the pieces it is captioned in, in order, each at most
    /// <see cref="PieceChars"/> characters: whole sentences while they fit,
    /// a sentence too long for one piece at its words, a word too long for
    /// one piece where it must be. Empty for a line with nothing in it.
    /// </summary>
    internal static List<string> Pieces(string? text)
    {
        var line = SingleSpaced(text);
        var pieces = new List<string>();

        if (line.Length == 0)
        {
            return pieces;
        }

        var piece = string.Empty;

        void Close()
        {
            if (piece.Length > 0)
            {
                pieces.Add(piece);
                piece = string.Empty;
            }
        }

        foreach (var sentence in Sentences(line))
        {
            if (piece.Length > 0 && piece.Length + 1 + sentence.Length <= PieceChars)
            {
                piece += " " + sentence;
                continue;
            }

            Close();

            if (sentence.Length <= PieceChars)
            {
                piece = sentence;
                continue;
            }

            foreach (var word in sentence.Split(' '))
            {
                if (piece.Length > 0 && piece.Length + 1 + word.Length <= PieceChars)
                {
                    piece += " " + word;
                    continue;
                }

                Close();

                var rest = word;

                while (rest.Length > PieceChars)
                {
                    pieces.Add(rest[..PieceChars]);
                    rest = rest[PieceChars..];
                }

                piece = rest;
            }
        }

        Close();

        if (pieces.Count > MostPieces)
        {
            pieces.RemoveRange(MostPieces, pieces.Count - MostPieces);

            var last = pieces[^1];
            pieces[^1] = (last.Length + 3 > PieceChars ? last[..(PieceChars - 3)].TrimEnd() : last) + "...";
        }

        return pieces;
    }

    /// <summary>The line's sentences, each with its own full stop, question or exclamation mark - split only where a space follows one.</summary>
    private static IEnumerable<string> Sentences(string line)
    {
        var start = 0;

        for (var i = 0; i < line.Length - 1; i++)
        {
            if ((line[i] is '.' or '!' or '?') && line[i + 1] == ' ')
            {
                yield return line[start..(i + 1)];
                start = i + 2;
            }
        }

        if (start < line.Length)
        {
            yield return line[start..];
        }
    }

    private static string SingleSpaced(string? text) =>
        string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Keeps one piece as a line, numbered now. Call under _gate.</summary>
    private CaptionLine KeepLocked(string text, string whole, int index, DateTime atUtc)
    {
        var line = new CaptionLine(++_next, text, atUtc) { WholeText = whole, PieceIndex = index };
        _lines.Enqueue(line);

        while (_lines.Count > Kept)
        {
            _lines.Dequeue();
        }

        return line;
    }

    /// <summary>Releases the waiting pieces that are due by <paramref name="nowUtc"/>, each at its own time. Call under _gate.</summary>
    private void ReleaseDueLocked(DateTime nowUtc)
    {
        while (_waiting.Count > 0 && _waiting.Peek().DueUtc <= nowUtc)
        {
            var (text, whole, index, due) = _waiting.Dequeue();
            KeepLocked(text, whole, index, due);
        }
    }

    /// <summary>Releases every waiting piece - the due ones at their own time, the rest now. Call under _gate.</summary>
    private void ReleaseWaitingLocked(DateTime nowUtc)
    {
        while (_waiting.Count > 0)
        {
            var (text, whole, index, due) = _waiting.Dequeue();
            KeepLocked(text, whole, index, due <= nowUtc ? due : nowUtc);
        }
    }
}
