namespace HeatCaller;

/// <summary>
/// What kind of sound is going out, for Race Control's "Now playing" (25 Oct
/// 2026). Higher wins when more than one is on at once - see
/// <see cref="NowPlaying.Pick"/>.
/// </summary>
internal enum NowPlayingKind
{
    /// <summary>The crowd ambience - under everything, most of the night.</summary>
    Crowd = 0,

    /// <summary>The music playlist.</summary>
    Music = 1,

    /// <summary>A music bed under the voice - the opening bed, the after-the-flag bed, the recap show's bed.</summary>
    Bed = 2,

    /// <summary>A sponsor video playing with its own sound.</summary>
    SponsorVideo = 3,

    /// <summary>
    /// A piece that is the thing the room is hearing right now: a jingle,
    /// sweeper, bumper or stinger, the recap show's opener or sting, a
    /// replay of the finish.
    /// </summary>
    Piece = 4,
}

/// <summary>One sound going out, as Race Control names it.</summary>
internal readonly record struct NowPlayingEntry(NowPlayingKind Kind, string Text, long Order);

/// <summary>
/// NOW PLAYING (25 Oct 2026). Every imaging piece and sound element that is
/// going out, so Race Control can say in cyan, at the right of its flag
/// row, what the room is hearing - and nothing when it is hearing nothing.
///
/// Two ways in. A piece that starts and ends (a jingle, a bed, a sponsor
/// video) says so with <see cref="Started"/> and <see cref="Ended"/>, from
/// whatever thread it is on - never from the audio thread itself, which
/// none of the callers is. Something that simply is or is not on (the
/// playlist, the crowd) is asked on the UI thread's clock tick through a
/// <see cref="Watch"/> - nothing is added to those players at all.
///
/// When several are on at once the most important is named, and of those
/// the newest - see <see cref="Pick"/>.
/// </summary>
internal static class NowPlaying
{
    /// <summary>How many characters of a name are kept; the row cuts it shorter with "…" when it has to.</summary>
    internal const int LongestText = 60;

    private static readonly object Gate = new();
    private static readonly List<(int Id, NowPlayingEntry Entry)> Playing = new();
    private static readonly Dictionary<string, Func<NowPlayingEntry?>> Watched = new(StringComparer.Ordinal);
    private static int _next;

    /// <summary>
    /// A sound has started; the number given back is handed to
    /// <see cref="Ended"/>. Safe from any thread.
    /// </summary>
    internal static int Started(NowPlayingKind kind, string? text)
    {
        lock (Gate)
        {
            var id = ++_next;
            Playing.Add((id, new NowPlayingEntry(kind, Tidy(text, kind), id)));
            return id;
        }
    }

    /// <summary>A sound has played out or been taken away. Safe to call twice, and with 0.</summary>
    internal static void Ended(int id)
    {
        if (id == 0)
        {
            return;
        }

        lock (Gate)
        {
            Playing.RemoveAll(p => p.Id == id);
        }
    }

    /// <summary>
    /// Something that is asked rather than told - the playlist, the crowd.
    /// Under a name, so a window built again replaces its own rather than
    /// adding a second. The function is called on the UI thread only, from
    /// <see cref="Shown"/>, and gives back null when its sound is not on.
    /// </summary>
    internal static void Watch(string name, Func<NowPlayingEntry?> now)
    {
        lock (Gate)
        {
            Watched[name] = now;
        }
    }

    /// <summary>Stops asking a watched sound - for a test, or a player going away for good.</summary>
    internal static void Unwatch(string name)
    {
        lock (Gate)
        {
            Watched.Remove(name);
        }
    }

    /// <summary>
    /// What Race Control names right now, or null for nothing. Call on the
    /// UI thread: the watched sounds are read here.
    /// </summary>
    internal static NowPlayingEntry? Shown => Pick(Everything());

    /// <summary>Every sound on now, told and asked, in no particular order. On the UI thread, as <see cref="Shown"/>.</summary>
    internal static List<NowPlayingEntry> Everything()
    {
        List<NowPlayingEntry> all;
        List<Func<NowPlayingEntry?>> asked;

        lock (Gate)
        {
            all = Playing.Select(p => p.Entry).ToList();
            asked = Watched.Values.ToList();
        }

        // Outside the lock: a player's own lock is taken in here, and
        // holding this one across it is how two locks deadlock.
        foreach (var ask in asked)
        {
            try
            {
                if (ask() is { } entry)
                {
                    all.Add(entry);
                }
            }
            catch
            {
                // A player being torn down - it is simply not named.
            }
        }

        return all;
    }

    /// <summary>
    /// The one to name: the most important kind (a piece over a sponsor
    /// video over a bed over the music over the crowd), and of those the one
    /// that started last. Deterministic - two of the same kind and order is
    /// settled by the text, so the row never flickers between them.
    /// </summary>
    internal static NowPlayingEntry? Pick(IEnumerable<NowPlayingEntry> entries)
    {
        NowPlayingEntry? best = null;

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Text))
            {
                continue;
            }

            if (best is not { } b
                || entry.Kind > b.Kind
                || (entry.Kind == b.Kind && entry.Order > b.Order)
                || (entry.Kind == b.Kind && entry.Order == b.Order && string.CompareOrdinal(entry.Text, b.Text) < 0))
            {
                best = entry;
            }
        }

        return best;
    }

    /// <summary>What the row says for an entry: its text, or blank for nothing.</summary>
    internal static string Describe(NowPlayingEntry? entry) => entry?.Text ?? string.Empty;

    /// <summary>A name that is never empty and never runs on - "Imaging" for one with no name.</summary>
    internal static string Tidy(string? text, NowPlayingKind kind)
    {
        var shown = string.IsNullOrWhiteSpace(text) ? KindWord(kind) : text.Trim();

        // One line: a file name with a line break in it would otherwise
        // push the row's text out of its box.
        shown = shown.Replace('\r', ' ').Replace('\n', ' ');

        return shown.Length > LongestText ? shown[..(LongestText - 1)].TrimEnd() + "…" : shown;
    }

    /// <summary>A kind in words, for a sound that came with no name of its own.</summary>
    internal static string KindWord(NowPlayingKind kind) => kind switch
    {
        NowPlayingKind.Crowd => "Crowd ambience",
        NowPlayingKind.Music => "Music",
        NowPlayingKind.Bed => "Music bed",
        NowPlayingKind.SponsorVideo => "Sponsor video",
        _ => "Imaging",
    };

    /// <summary>"What · name", or just one of them when the other is missing.</summary>
    internal static string Joined(string? what, string? name)
    {
        var w = what?.Trim() ?? string.Empty;
        var n = name?.Trim() ?? string.Empty;

        return w.Length == 0 ? n : n.Length == 0 ? w : $"{w} · {n}";
    }
}
