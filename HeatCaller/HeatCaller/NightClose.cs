namespace HeatCaller;

/// <summary>
/// What is still to do once the racing is over (11 Nov 2026, race-night
/// build plan, Phase 2, "close the night").
///
/// WHY: the end of the night is when things get forgotten. Everybody is
/// packing up, the director is tired, and the last main's result is the
/// only thing anybody is thinking about. The rental transponder that went
/// home in someone's pocket, the results that never reached the website,
/// the night emails that were never sent and the backup that was never
/// taken all come from this hour. The Next Steps card already says "The
/// program is complete." This adds the list of what is left before lights
/// out, from things the app already knows.
///
/// Pure: the facts are gathered by whoever can see the stores (MainForm)
/// and handed in.
/// </summary>
public static class NightClose
{
    /// <summary>What the app knows at the end of the night. Null parts are things it can't tell.</summary>
    /// <param name="RentalsOut">Who still has a rental transponder (one name per unit out).</param>
    /// <param name="NightEmailsToSend">Night emails still to go out; null when night emails are off or can't be planned.</param>
    /// <param name="ResultsNotOnline">Tonight's results the club website hasn't got yet; null when publishing is off.</param>
    /// <param name="LastBackupUtc">When the last backup was taken; null when there has never been one.</param>
    /// <param name="DayOpen">Whether the race day is still open.</param>
    /// <param name="Owing">Drivers who raced and still owe their entry (11 Nov 2026, Phase 4); null when the day has no fee.</param>
    public sealed record Facts(
        IReadOnlyList<string> RentalsOut,
        int? NightEmailsToSend,
        int? ResultsNotOnline,
        DateTime? LastBackupUtc,
        bool DayOpen,
        IReadOnlyList<string>? Owing = null);

    /// <summary>
    /// The list, as the Next Steps card says it, starting with a space:
    /// " Before lights out: 2 rentals still out (Sam, Jo) · 14 night emails
    /// to send · back up tonight's results · Close Day." With nothing left:
    /// " All wrapped up - lights out."
    /// </summary>
    /// <param name="facts">What the app knows.</param>
    /// <param name="lastRaceEndedUtc">When tonight's last race finished; a backup from before it is missing results.</param>
    public static string Note(Facts facts, DateTime? lastRaceEndedUtc)
    {
        var left = Left(facts, lastRaceEndedUtc);

        return left.Count == 0
            ? " All wrapped up - lights out."
            : $" Before lights out: {string.Join(" · ", left)}.";
    }

    /// <summary>Each thing still to do, in the order to do it.</summary>
    public static List<string> Left(Facts facts, DateTime? lastRaceEndedUtc)
    {
        var left = new List<string>();

        if (facts.RentalsOut.Count > 0)
        {
            var names = facts.RentalsOut.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var who = names.Count == 0 ? string.Empty : $" ({string.Join(", ", names.Take(3))}{(names.Count > 3 ? ", ..." : string.Empty)})";
            left.Add($"{Plural.Of(facts.RentalsOut.Count, "rental")} still out{who}");
        }

        if (facts.Owing is { Count: > 0 } owing)
        {
            var names = string.Join(", ", owing.Take(3)) + (owing.Count > 3 ? ", ..." : string.Empty);
            left.Add($"{Plural.Of(owing.Count, "driver")} still {(owing.Count == 1 ? "owes" : "owe")} their entry ({names})");
        }

        if (facts.ResultsNotOnline is > 0 and var owed)
        {
            left.Add($"{Plural.Of(owed, "result")} not on the club website yet");
        }

        if (facts.NightEmailsToSend is > 0 and var emails)
        {
            left.Add($"{Plural.Of(emails, "night email")} to send");
        }

        // A backup taken before the last race finished hasn't got it.
        var backedUp = facts.LastBackupUtc is { } backup
                       && (lastRaceEndedUtc is not { } ended || backup >= ended);

        if (!backedUp)
        {
            left.Add("back up tonight's results");
        }

        if (facts.DayOpen)
        {
            left.Add("Close Day");
        }

        return left;
    }
}

/// <summary>What a line on the closing checklist is about, so the window knows which screen sorts it.</summary>
internal enum CloseNightKind
{
    Rentals,
    Money,
    Website,
    NightEmails,
    Backup,
    CloseDay,

    /// <summary>One of the club's own lines ("Lock the clubhouse"), ticked by hand.</summary>
    Own,
}

/// <summary>
/// The closing checklist as a list you tick off (11 Nov 2026) - the same
/// checks as NightClose.Left, plus the club's own lines, each one either
/// seen done by the app or ticked by hand.
///
/// WHY a second shape and not just the sentence: a sentence only says what
/// is left. A director who has dealt with something the app can't see - the
/// rental handed over the fence, the entry paid to the treasurer by bank
/// transfer, the backup on the club's own drive - had no way to say so, and
/// the card kept nagging about it. Ticking it off makes it go away.
///
/// Optional: AppSettings.UseCloseNightChecklist. Off, the card reads exactly
/// as before, a single "Before lights out" line.
///
/// Pure, like NightClose: the facts and the ticks are handed in.
/// </summary>
internal static class CloseNightChecklist
{
    /// <summary>One line on the list.</summary>
    /// <param name="Key">Stable name, for remembering a tick ("rentals", "own:Lock the clubhouse").</param>
    /// <param name="SeenDone">The app can see it's done.</param>
    /// <param name="Ticked">Ticked off by hand.</param>
    internal sealed record Item(string Key, CloseNightKind Kind, string Words, bool SeenDone, bool Ticked)
    {
        public bool Done => SeenDone || Ticked;
    }

    /// <summary>Prefix of an own line's key.</summary>
    private const string OwnPrefix = "own:";

    /// <summary>
    /// Every line, in the order to do them: the app's checks first, then the
    /// club's own. A check the app can't make tonight (no entry fee, the
    /// club website off, night emails off) isn't listed at all - it isn't
    /// something left undone.
    /// </summary>
    internal static List<Item> Build(
        NightClose.Facts facts,
        DateTime? lastRaceEndedUtc,
        IEnumerable<string>? ownLines,
        IReadOnlySet<string> ticked)
    {
        var items = new List<Item>();

        void Add(string key, CloseNightKind kind, bool seenDone, string done, string left) =>
            items.Add(new Item(key, kind, seenDone ? done : left, seenDone, !seenDone && ticked.Contains(key)));

        var rentalNames = facts.RentalsOut.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Add("rentals", CloseNightKind.Rentals, facts.RentalsOut.Count == 0,
            "Every rental is back",
            $"{Plural.Of(facts.RentalsOut.Count, "rental")} still out{Names(rentalNames)}");

        if (facts.Owing is { } owing)
        {
            Add("money", CloseNightKind.Money, owing.Count == 0,
                "Everybody has paid",
                $"{Plural.Of(owing.Count, "driver")} still {(owing.Count == 1 ? "owes" : "owe")} their entry{Names(owing)}");
        }

        if (facts.ResultsNotOnline is { } notOnline)
        {
            Add("website", CloseNightKind.Website, notOnline == 0,
                "Tonight's results are on the club website",
                $"{Plural.Of(notOnline, "result")} not on the club website yet");
        }

        if (facts.NightEmailsToSend is { } emails)
        {
            Add("emails", CloseNightKind.NightEmails, emails == 0,
                "Night emails sent",
                $"{Plural.Of(emails, "night email")} to send");
        }

        var backedUp = facts.LastBackupUtc is { } backup
                       && (lastRaceEndedUtc is not { } ended || backup >= ended);
        Add("backup", CloseNightKind.Backup, backedUp,
            "Tonight's results are backed up",
            "back up tonight's results");

        Add("closeday", CloseNightKind.CloseDay, !facts.DayOpen,
            "The race day is closed",
            "Close Day");

        foreach (var line in Own(ownLines))
        {
            var key = OwnPrefix + line;
            items.Add(new Item(key, CloseNightKind.Own, line, false, ticked.Contains(key)));
        }

        return items;
    }

    /// <summary>The card's line, from the list: what is left, or all wrapped up. Starts with a space, like NightClose.Note.</summary>
    internal static string Note(IReadOnlyList<Item> items)
    {
        var left = items.Where(i => !i.Done).Select(i => i.Words).ToList();

        return left.Count == 0
            ? " All wrapped up - lights out."
            : $" Before lights out: {string.Join(" · ", left)}.";
    }

    /// <summary>The club's own lines, tidied: trimmed, blanks and repeats gone.</summary>
    internal static List<string> Own(IEnumerable<string>? lines) =>
        (lines ?? Array.Empty<string>())
        .Select(l => l.Trim())
        .Where(l => l.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>The ticks saved for this race day, out of AppSettings.CloseNightTicks ("dayId|key").</summary>
    internal static HashSet<string> TicksFor(IEnumerable<string>? saved, string dayId)
    {
        var prefix = dayId + "|";

        return (saved ?? Array.Empty<string>())
            .Where(s => s.StartsWith(prefix, StringComparison.Ordinal))
            .Select(s => s[prefix.Length..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The saved ticks with one ticked or unticked. Only tonight's are kept:
    /// last week's ticks are no use to anybody, and the list would only grow.
    /// </summary>
    internal static List<string> Tick(IEnumerable<string>? saved, string dayId, string key, bool on)
    {
        var tonight = TicksFor(saved, dayId);

        if (on)
        {
            tonight.Add(key);
        }
        else
        {
            tonight.Remove(key);
        }

        return tonight.OrderBy(k => k, StringComparer.Ordinal).Select(k => dayId + "|" + k).ToList();
    }

    private static string Names(IReadOnlyList<string> names) =>
        names.Count == 0
            ? string.Empty
            : $" ({string.Join(", ", names.Take(3))}{(names.Count > 3 ? ", ..." : string.Empty)})";
}
