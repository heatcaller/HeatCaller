namespace HeatCaller;

/// <summary>
/// THE DESK INBOX (15 Nov 2026, Asher: "driver requests, club notes, votes
/// need to all be on one screen for the race director that's easy to
/// manage ... based on the best practices for managing a stream of requests
/// quickly and easily", then "inbox replaces old as long as there's no
/// missing functionality", "online sign ups inbox as well"). The plan is
/// plans/relay-themes-and-desk-inbox-plan.md, section 4.
///
/// What arrives from the room - a driver's request from their phone, a club
/// note from the room, a sign-up - is one list here, in the order it needs
/// doing rather than the order it came: anything that holds up the next race
/// first. This file is the list itself, with no window: what an item is,
/// the order, how long it has waited, and which ones are snoozed. The window
/// (DeskInboxForm) draws it and does what the desks already did.
///
/// No new storage: every item is read from the desk that already owns it -
/// DriverRequestDesk, ClubNotes, RelaySignupDesk - and every answer goes
/// back through that desk, which keeps every gate it had. The room vote is
/// not an item: it is something live that needs closing, so the window shows
/// it as a card above the list.
/// </summary>
internal static class DeskInbox
{
    /// <summary>A request kind that holds up the next race when it waits: a driver late, skipping, or a marshal swap.</summary>
    internal static bool RaceBlockingKind(string kind) =>
        kind is DriverAsk.Late or DriverAsk.Skip or DriverAsk.Cover or DriverAsk.Swap;

    /// <summary>
    /// The list in the order the desk works it: race-blocking first (late,
    /// skip, marshal swaps, and result queries still inside a protest
    /// window, whose answer can still change a result), then everything
    /// else; oldest first within each, because the oldest has waited
    /// longest. Snoozed items go to the foot, in the same order.
    /// </summary>
    internal static List<InboxItem> Order(IEnumerable<InboxItem> items, ISet<string>? snoozed = null) =>
        items
            .Select((item, i) => (item, i))
            .OrderBy(x => snoozed is not null && snoozed.Contains(x.item.Key) ? 1 : 0)
            .ThenBy(x => x.item.RaceBlocking ? 0 : 1)
            .ThenBy(x => x.item.SinceUtc)
            .ThenBy(x => x.i)
            .Select(x => x.item)
            .ToList();

    /// <summary>The items one filter chip leaves.</summary>
    internal static List<InboxItem> Filter(IEnumerable<InboxItem> items, InboxFilter filter) =>
        items.Where(item => Shows(filter, item)).ToList();

    /// <summary>Whether a chip shows an item.</summary>
    internal static bool Shows(InboxFilter filter, InboxItem item) => filter switch
    {
        InboxFilter.All => true,
        InboxFilter.RaceBlocking => item.RaceBlocking,
        InboxFilter.Requests => item.Source == InboxSource.Request,
        InboxFilter.Queries => item.Source == InboxSource.Request && item.Kind == DriverAsk.Query,
        InboxFilter.Crew => item.Source == InboxSource.Request && item.Kind == DriverAsk.Crew,
        InboxFilter.Notes => item.Source == InboxSource.Note,
        InboxFilter.Signups => item.Source == InboxSource.Signup,
        InboxFilter.Vote => false,
        _ => true,
    };

    /// <summary>The words on a chip.</summary>
    internal static string ChipWords(InboxFilter filter) => filter switch
    {
        InboxFilter.All => "All",
        InboxFilter.RaceBlocking => "Race-blocking",
        InboxFilter.Requests => "Requests",
        InboxFilter.Queries => "Result queries",
        InboxFilter.Crew => "Crew and notes",
        InboxFilter.Notes => "Club notes",
        InboxFilter.Signups => "Sign-ups",
        InboxFilter.Vote => "Vote",
        _ => filter.ToString(),
    };

    /// <summary>A chip with its count: "Requests 3", or just "Requests" with none.</summary>
    internal static string ChipText(InboxFilter filter, int count) =>
        filter == InboxFilter.Vote || count == 0 ? ChipWords(filter) : $"{ChipWords(filter)} {Math.Min(count, 99)}";

    /// <summary>How long one has waited, in the fewest characters: "now", "4m", "1h 5m".</summary>
    internal static string Waited(TimeSpan waited)
    {
        if (waited < TimeSpan.FromMinutes(1))
        {
            return "now";
        }

        if (waited < TimeSpan.FromHours(1))
        {
            return $"{(int)waited.TotalMinutes}m";
        }

        if (waited < TimeSpan.FromDays(1))
        {
            return waited.Minutes == 0 ? $"{(int)waited.TotalHours}h" : $"{(int)waited.TotalHours}h {waited.Minutes}m";
        }

        return $"{(int)waited.TotalDays}d";
    }

    /// <summary>
    /// How late an item is getting: fine, amber, then red. A race-blocking
    /// one is late sooner - the next race is minutes away.
    /// </summary>
    internal static InboxAge Age(InboxItem item, DateTime nowUtc)
    {
        var waited = nowUtc - item.SinceUtc;
        var (amber, red) = item.RaceBlocking
            ? (TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5))
            : (TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));

        return waited >= red ? InboxAge.Red : waited >= amber ? InboxAge.Amber : InboxAge.Fine;
    }

    /// <summary>The badge's colour on Race Control's Inbox button: red while anything race-blocking waits.</summary>
    internal static bool AnyRaceBlocking(IEnumerable<InboxItem> items, ISet<string>? snoozed = null) =>
        items.Any(item => item.RaceBlocking && (snoozed is null || !snoozed.Contains(item.Key)));

    /// <summary>
    /// The quick replies offered when a request is declined, before "Other
    /// reason...". The club can change them; these are what a new club has.
    /// </summary>
    internal static readonly IReadOnlyList<string> DefaultQuickReplies = new[]
    {
        "Ask at the desk",
        "After this heat",
        "Already changed",
        "Not tonight",
    };

    /// <summary>The club's quick replies: their own when they have any, else the defaults. Blank lines dropped, at most eight.</summary>
    internal static IReadOnlyList<string> QuickReplies(IEnumerable<string>? club)
    {
        var own = (club ?? Array.Empty<string>())
            .Select(r => (r ?? string.Empty).Trim())
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();

        return own.Count > 0 ? own : DefaultQuickReplies;
    }
}

/// <summary>Where an inbox item came from.</summary>
internal enum InboxSource
{
    Request,
    Note,
    Signup,
}

/// <summary>The chips across the top of the inbox.</summary>
internal enum InboxFilter
{
    All,
    RaceBlocking,
    Requests,
    Queries,
    Crew,
    Notes,
    Signups,

    /// <summary>Opens on the vote card: the list as All, the vote card brought to the eye.</summary>
    Vote,
}

/// <summary>How long an item has waited, as a colour.</summary>
internal enum InboxAge
{
    Fine,
    Amber,
    Red,
}

/// <summary>
/// One thing waiting for the desk, whichever desk it is from.
/// </summary>
/// <param name="Source">Which desk owns it.</param>
/// <param name="Id">Its id in that desk: a request's id, a note's id, a sign-up's hash.</param>
/// <param name="Kind">A request's kind (DriverAsk); "note" or "signup" for the others.</param>
/// <param name="Badge">The short word in the list's kind column: "Late", "Note", "Sign-up"...</param>
/// <param name="From">Who it is from.</param>
/// <param name="What">What they asked for, in one line.</param>
/// <param name="Note">Anything the desk should know about it.</param>
/// <param name="SinceUtc">When it arrived.</param>
/// <param name="RaceBlocking">Whether it holds up the next race.</param>
/// <param name="Payload">The desk's own object, for the answer.</param>
internal sealed record InboxItem(
    InboxSource Source,
    string Id,
    string Kind,
    string Badge,
    string From,
    string What,
    string Note,
    DateTime SinceUtc,
    bool RaceBlocking,
    object? Payload)
{
    /// <summary>Unique across every desk: the source and its id.</summary>
    public string Key => $"{Source}:{Id}";
}

/// <summary>
/// Items put off with Later (S): out of the way at the foot of the list until
/// the race now on is over, or ten minutes, whichever comes first - so a
/// snoozed item can never be lost for the night.
/// </summary>
internal sealed class InboxSnooze
{
    /// <summary>The longest anything stays snoozed.</summary>
    internal static readonly TimeSpan Longest = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, DateTime> _until = new(StringComparer.Ordinal);

    /// <summary>Puts one off until the race is over, or ten minutes from now.</summary>
    internal void Snooze(string key, DateTime nowUtc) => _until[key] = nowUtc + Longest;

    /// <summary>Brings one back now.</summary>
    internal void Wake(string key) => _until.Remove(key);

    /// <summary>The race now on is over: everything snoozed comes back.</summary>
    internal void WakeAll() => _until.Clear();

    /// <summary>The keys still snoozed at this moment; any whose time is up are woken.</summary>
    internal HashSet<string> Snoozed(DateTime nowUtc)
    {
        foreach (var key in _until.Where(kv => kv.Value <= nowUtc).Select(kv => kv.Key).ToList())
        {
            _until.Remove(key);
        }

        return new HashSet<string>(_until.Keys, StringComparer.Ordinal);
    }

    /// <summary>Whether one is snoozed now.</summary>
    internal bool IsSnoozed(string key, DateTime nowUtc) => _until.TryGetValue(key, out var until) && until > nowUtc;
}
