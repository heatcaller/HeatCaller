namespace HeatCaller;

/// <summary>One line the announcer said, as a caption.</summary>
/// <param name="N">Its number, counting up from 1 for as long as the app runs, so a page can tell a new line from one it has shown.</param>
/// <param name="Text">What was said, on one line, at most <see cref="CaptionLog.MaxChars"/> characters.</param>
/// <param name="AtUtc">When it was heard.</param>
public sealed record CaptionLine(long N, string Text, DateTime AtUtc);

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
/// </remarks>
internal sealed class CaptionLog
{
    /// <summary>Lines kept. Enough for any reader; a caption older than this has long scrolled away.</summary>
    public const int Kept = 12;

    /// <summary>The longest line kept. A longer one is cut at a word, with "..." after it.</summary>
    public const int MaxChars = 300;

    /// <summary>The one log the whole app shares.</summary>
    public static CaptionLog Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Queue<CaptionLine> _lines = new();
    private long _next;

    /// <summary>
    /// Adds a line as heard now (or at <paramref name="atUtc"/>), and returns
    /// it as kept. Null for a line with nothing in it, which is not kept.
    /// </summary>
    public CaptionLine? Add(string? text, DateTime? atUtc = null)
    {
        var clean = Clean(text);

        if (clean.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            var line = new CaptionLine(++_next, clean, atUtc ?? DateTime.UtcNow);
            _lines.Enqueue(line);

            while (_lines.Count > Kept)
            {
                _lines.Dequeue();
            }

            return line;
        }
    }

    /// <summary>The last <paramref name="count"/> lines (at most <see cref="Kept"/>), oldest first.</summary>
    public IReadOnlyList<CaptionLine> Last(int count = Kept)
    {
        lock (_gate)
        {
            return count <= 0
                ? Array.Empty<CaptionLine>()
                : _lines.Skip(Math.Max(0, _lines.Count - count)).ToList();
        }
    }

    /// <summary>The newest line's number, or 0 before anything has been said. Cheap: a page can ask every second.</summary>
    public long LatestN
    {
        get
        {
            lock (_gate)
            {
                return _lines.Count == 0 ? 0 : _lines.Last().N;
            }
        }
    }

    /// <summary>Forgets every line. The numbers carry on from where they were, so a reader never mistakes a new line for an old one.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
        }
    }

    /// <summary>One line, single-spaced, at most <see cref="MaxChars"/> characters.</summary>
    internal static string Clean(string? text)
    {
        var line = string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (line.Length <= MaxChars)
        {
            return line;
        }

        // At the last space that leaves room for the dots, unless that would
        // throw most of the line away.
        var room = MaxChars - 3;
        var cut = line.LastIndexOf(' ', room);

        return (cut >= room / 2 ? line[..cut] : line[..room]).TrimEnd() + "...";
    }
}
